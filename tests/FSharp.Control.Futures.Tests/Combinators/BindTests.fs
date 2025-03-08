module FSharp.Control.Futures.Tests.Combinators.Bind

open System
open Xunit
open FSharp.Control.Futures

[<Fact>]
let ``Future.bind combine computation``() =
    let first = Future.yieldWorkflow () |> Future.bind (fun () -> Future.unit')
    let second = Future.yieldWorkflow () |> Future.bind (fun () -> Future.ready 8)

    let fut =
        first
        |> Future.bind (fun () -> second)
        |> Future.bind (fun x -> Future.ready (x*x))

    let x = Future.runBlocking fut

    Assert.Equal(64, x)
    ()

[<Fact>]
let ``Future.bind throws exception``() =
    let yielded () = Future.yieldWorkflow () |> Future.bind (fun () -> Future.unit')

    let exInBinder = yielded () |> Future.bind (fun () -> raise (Exception ""); Future.ready 12) |> Future.ignore
    let exInFirst = Future.lazy' (fun () -> raise (Exception "")) |> Future.bind (fun () -> Future.ready 12) |> Future.ignore
    let exInSecond = yielded () |> Future.bind (fun () -> Future.lazy' (fun () -> raise (Exception ""); 12)) |> Future.ignore

    Assert.ThrowsAny(fun () -> Future.runBlocking exInBinder) |> ignore
    Assert.ThrowsAny(fun () -> Future.runBlocking exInFirst) |> ignore
    Assert.ThrowsAny(fun () -> Future.runBlocking exInSecond) |> ignore
    ()
