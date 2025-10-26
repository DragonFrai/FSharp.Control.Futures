module FSharp.Control.Futures.Actors.Examples.Program

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors
open FSharp.Control.Futures.Actors.Addressing
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync


type HelloActor() =
    inherit HandlerActor()

    interface IHandler<string ,string> with
        member this.Handle(msg, accept) = future {
            accept.Reply($"Hello, {msg}!")
        }

let actorMailbox = ActorMailbox()
actorMailbox.SetActor(HelloActor())

let addr: IDynamicAddress = actorMailbox
let addrMkHello = addr.Narrow<string, string>()

ThreadPoolRuntime.instance.Spawn(actorMailbox.Start()) |> ignore

future {
    let! r = addrMkHello.Send("Name1")
    do printfn $"> {r}"
    do! Future.sleepMs 1000
    let! r = addrMkHello.Send("Name2")
    do printfn $"> {r}"
    do! Future.sleepMs 1000
    let! r = addrMkHello.Send("Name3")
    do printfn $"> {r}"
    do! Future.sleepMs 1000
    let! r = addrMkHello.Send("Name4")
    do printfn $"> {r}"
    do! Future.sleepMs 1000
    let! r = addrMkHello.Send("Name5")
    do printfn $"> {r}"
} |> Future.runBlocking
