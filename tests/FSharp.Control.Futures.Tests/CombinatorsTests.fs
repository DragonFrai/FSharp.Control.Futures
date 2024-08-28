module FSharp.Control.Futures.Tests.CombinatorsTests

open System
open Expecto
open FSharp.Control.Futures

let bindRegular = test "Future.bind combine computation" {
    let first = Future.yieldWorkflow () |> Future.bind (fun () -> Future.unit')
    let second = Future.yieldWorkflow () |> Future.bind (fun () -> Future.ready 8)

    let fut =
        first
        |> Future.bind (fun () -> second)
        |> Future.bind (fun x -> Future.ready (x*x))

    let x = Future.runBlocking fut

    Expect.equal x 64 "bindRegular return illegal value"
    ()
}

let bindException = test "Future.bind throws exception" {
    let yielded () = Future.yieldWorkflow () |> Future.bind (fun () -> Future.unit')

    let exInBinder = yielded () |> Future.bind (fun () -> raise (Exception ""); Future.ready 12) |> Future.ignore
    let exInFirst = Future.lazy' (fun () -> raise (Exception "")) |> Future.bind (fun () -> Future.ready 12) |> Future.ignore
    let exInSecond = yielded () |> Future.bind (fun () -> Future.lazy' (fun () -> raise (Exception ""); 12)) |> Future.ignore

    Expect.throws (fun () -> Future.runBlocking exInBinder) "Exception in binder not throws"
    Expect.throws (fun () -> Future.runBlocking exInFirst) "Exception in source future not throws"
    Expect.throws (fun () -> Future.runBlocking exInSecond) "Exception in binder future not throws"
    ()
}

[<Tests>]
let testsBind =
    testList "bind" [
        bindRegular
        bindException
    ]
