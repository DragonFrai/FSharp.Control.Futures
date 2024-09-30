open FSharp.Control.Futures
open FSharp.Control.Futures.Actors
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync



type HelloActor() =
    inherit HandlerActor()

    interface IHandler<int ,string> with
        member this.Handle(_ctx, msg) = future {
            printfn $"Hello, {msg.Msg}!"
            msg.Reply.Send("dd")
        }

let arb = Arbiter(fun () -> HelloActor())

let addr = arb.Start(ThreadPoolRuntime.instance)

let os = OneShotImpl<string>()
do addr.Post(Msg(12, os)) |> Future.runBlocking
let r = os |> Future.runBlocking
printfn $"Reply is {r}"

addr.Narrow<string, string>().Send("hello") |> Future.runBlocking
