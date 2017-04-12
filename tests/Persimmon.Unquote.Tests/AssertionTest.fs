module Persimmon.Unquote.Tests.AssertionTest

open System
open Persimmon
open UseTestNameByReflection

let ``unquote test`` = test {
  do!
    match Unquote.assertPred <@ ([3; 2; 1; 0] |> List.map ((+) 1)) = [1 + 3..1 + 0] @> with
    | NotPassed(None, Violated msg) ->
      msg.Split([|"\r\n";"\r";"\n"|], StringSplitOptions.None)
      |> Array.toList
      |> assertEquals ["([3; 2; 1; 0] |> List.map ((+) 1)) = [1 + 3..1 + 0]"; "[4; 3; 2; 1] = [4..1]"; "[4; 3; 2; 1] = []"; "false"]
    | x ->
      sprintf "Expected NotPassed, but was: %A" x
      |> fail
}

module Trap =

  open Unquote

  exception TestException of string

  let trapExn () = trap { it(raise (TestException "test")) }

let ``trap exn`` = test {
  let! e = Trap.trapExn ()
  do!
    e.GetType()
    |> assertEquals typeof<Trap.TestException>
}
