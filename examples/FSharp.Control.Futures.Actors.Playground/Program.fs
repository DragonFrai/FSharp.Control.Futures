module FSharp.Control.Futures.Actors.Examples.Program

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync


type HelloActor() =
    inherit HandlerActor()

    interface IHandler<string ,string> with
        member this.Handle(_ctx, msg) = future {
            msg.Reply.Send($"Hello, {msg.Msg}!") |> ignore
        }

let arb = Arbiter.Start({
    Actor = HelloActor()
    ActorId = ActorId("Hello")
    MessageLoopRuntime = ThreadPoolRuntime.instance
    BackgroundRuntime = ThreadPoolRuntime.instance
})

let addr = arb.Address

let os = OneShot<string>()
do addr.SendMsg(Msg("Steve", os.Sender)) |> Future.runBlocking
let r = os.Receive() |> Future.runBlocking
printfn $"Reply is '{r}'"

future {
    let! r = addr.Narrow<string, string>().Send("hello")
    do printfn $"> {r}"
} |> Future.runBlocking
