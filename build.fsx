#r @"packages/build/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System
open System.IO
#if MONO
#else
#load "packages/build/SourceLink.Fake/tools/Fake.fsx"
open SourceLink
#endif

let configuration = getBuildParamOrDefault "configuration" "Release"

let outDir = "bin"

let project = "Persimmon.Unquote"

let solutionFile = "Persimmon.Unquote.sln"

let net45Project = "src/Persimmon.Unquote/Persimmon.Unquote.fsproj"
let netCoreProject = "src/Persimmon.Unquote.NETCore/Persimmon.Unquote.NETCore.fsproj"
let net45TestProject = "tests/Persimmon.Unquote.Tests/Persimmon.Unquote.Tests.fsproj"
let netCoreTestProject = "tests/Persimmon.Unquote.NETCore.Tests/Persimmon.Unquote.NETCore.Tests.fsproj"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "persimmon-projects"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "Persimmon.Unquote"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/persimmon-projects"

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|) (projFileName:string) =
  match projFileName with
  | f when f.EndsWith("fsproj") -> Fsproj
  | f when f.EndsWith("csproj") -> Csproj
  | f when f.EndsWith("vbproj") -> Vbproj
  | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
  let getAssemblyInfoAttributes projectName =
    [ Attribute.Title (projectName |> replace ".Portable259" "")
      Attribute.Product project
      Attribute.Version release.AssemblyVersion
      Attribute.FileVersion release.AssemblyVersion
      Attribute.InformationalVersion release.NugetVersion ]

  let getProjectDetails projectPath =
    let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
    ( projectPath,
      projectName,
      System.IO.Path.GetDirectoryName(projectPath),
      (getAssemblyInfoAttributes projectName)
    )

  !! "src/**/*.??proj"
  |> Seq.choose (fun p ->
    let name = Path.GetFileNameWithoutExtension(p)
    if name.EndsWith("Portable259") then getProjectDetails p |> Some
    else None
  )
  |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
    match projFileName with
    | Fsproj -> CreateFSharpAssemblyInfo (("src" @@ folderName) @@ "AssemblyInfo.fs") attributes
    | Csproj -> CreateCSharpAssemblyInfo ((folderName @@ "Properties") @@ "AssemblyInfo.cs") attributes
    | Vbproj -> CreateVisualBasicAssemblyInfo ((folderName @@ "My Project") @@ "AssemblyInfo.vb") attributes
  )
)

// Copies binaries from default VS location to exepcted bin folder
// But keeps a subdirectory structure for each project in the
// src folder to support multiple project outputs
Target "CopyBinaries" (fun _ ->
  !! "src/**/*.??proj"
  |> Seq.filter (fun p ->
    let p = Path.GetFileNameWithoutExtension(p)
    p.EndsWith("Portable259")
  )
  |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin" @@ configuration, outDir @@ (System.IO.Path.GetFileNameWithoutExtension f)))
  |>  Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
  CleanDirs ["bin"; "temp"]
  !! ("./src/**/bin" @@ configuration)
  |> CleanDirs
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->

  !! solutionFile
  |> MSBuild "" "Rebuild" [ ("Configuration", configuration) ]
  |> ignore

  let args = [ sprintf "/p:Version=%s" release.NugetVersion ]

  DotNetCli.Restore (fun p ->
    { p with
        Project = net45Project
    }
  )
  DotNetCli.Build (fun p ->
    { p with
        Project = net45Project
        Configuration = configuration
        AdditionalArgs = args
    }
  )

  DotNetCli.Restore (fun p ->
    { p with
        Project = netCoreProject
    }
  )
  DotNetCli.Build (fun p ->
    { p with
        Project = netCoreProject
        Configuration = configuration
        AdditionalArgs = args
    }
  )

  DotNetCli.Restore (fun p ->
    { p with
        Project = net45TestProject
    }
  )
  DotNetCli.Build (fun p ->
    { p with
        Project = net45TestProject
        Configuration = configuration
        AdditionalArgs = args
    }
  )

  DotNetCli.Restore (fun p ->
    { p with
        Project = netCoreTestProject
    }
  )
  DotNetCli.Build (fun p ->
    { p with
        Project = netCoreTestProject
        Configuration = configuration
        AdditionalArgs = args
    }
  )
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner
Target "RunTests" (fun _ ->
  DotNetCli.Test (fun p ->
    { p with
        Project = net45TestProject
        Configuration = configuration
    }
  )
  DotNetCli.Test (fun p ->
    { p with
        Project = netCoreTestProject
        Configuration = configuration
    }
  )
)

#if MONO
#else
// --------------------------------------------------------------------------------------
// SourceLink allows Source Indexing on the PDB generated by the compiler, this allows
// the ability to step through the source code of external libraries https://github.com/ctaggart/SourceLink

Target "SourceLink" (fun _ ->
  let baseUrl = sprintf "%s/%s/{0}/%%var2%%" gitRaw project
  !! "src/**/*.??proj"
  -- "src/**/*.shproj"
  |> Seq.iter (fun projFile ->
    let proj = VsProj.LoadRelease projFile
    SourceLink.Index proj.CompilesNotLinked proj.OutputFilePdb __SOURCE_DIRECTORY__ baseUrl
  )
)

#endif

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->

  NuGet (fun p ->
    {
      p with
        OutputPath = outDir
        WorkingDir = outDir
        Version = release.NugetVersion
        ReleaseNotes = toLines release.Notes
    }
  ) "src/Persimmon.Unquote.nuspec"
)

Target "PublishNuget" (fun _ ->
  Paket.Push(fun p ->
    { p with
        WorkingDir = outDir })
)

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target "Release" (fun _ ->
  StageAll ""
  Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
  Branches.push ""

  Branches.tag "" release.NugetVersion
  Branches.pushTag "" "origin" release.NugetVersion

  // release on github
  createClient (getBuildParamOrDefault "github-user" "") (getBuildParamOrDefault "github-pw" "")
  |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
  // TODO: |> uploadFile "PATH_TO_FILE"
  |> releaseDraft
  |> Async.RunSynchronously
)

Target "All" DoNothing

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
  ==> "RunTests"
  ==> "All"

"All"
#if MONO
#else
  =?> ("SourceLink", Pdbstr.tryFind().IsSome )
#endif
  ==> "NuGet"

"NuGet"
  ==> "PublishNuget"
  ==> "Release"

RunTargetOrDefault "All"
