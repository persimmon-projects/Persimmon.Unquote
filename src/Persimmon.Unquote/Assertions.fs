namespace Persimmon

open System
open System.Reflection
#if NET45 || PORTABLE
open System.Runtime.CompilerServices
#endif
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Persimmon
open Swensen.Unquote

[<NoEquality; NoComparison; Sealed>]
type UnquoteAssert =

  static member Pred(expr:Expr<bool>, [<CallerLineNumber>]?line : int) =
    let u = unquote expr
    match u.FinalReduction with
    | DerivedPatterns.Bool(true) -> Assert.Pass()
    | _ ->
      let msg =
        u.DecompiledReductions
        |> String.concat Environment.NewLine
      Assert.Fail(msg, ?line = line)

module Unquote =

  let private (|MethodCall|_|) name (info: MethodInfo) =
    if info.Name = name then Some ()
    else None

  type TrapBuilder with
    member __.Quote() = ()
    member __.Run(expr: Expr<unit -> _>) =
      let u =
        match expr with
        // <@ b.Delay(fun () -> trap.It(trap.Yield(), f)) >@
        | Call(Some _, MethodCall "Delay", [Lambda(_, Call(Some _, MethodCall "It", [Call(Some _, MethodCall "Yield", _); expr]))]) ->
          unquote expr
        | _ -> failwithf "Invald Expr: %A" expr
      match u.ReductionException with
      | Some e -> pass e
      | None -> fail "Expect thrown exn but not"

  let assertPred (expr:Expr<bool>) = UnquoteAssert.Pred(expr, ?line = None)

  let inline (=!) x y = assertPred <@ x = y @>
  let inline (<!) x y = assertPred <@ x < y @>
  let inline (>!) x y = assertPred <@ x > y @>
  let inline (<=!) x y = assertPred <@ x <= y @>
  let inline (>=!) x y = assertPred <@ x >= y @>
  let inline (<>!) x y = assertPred <@ x <> y @>
