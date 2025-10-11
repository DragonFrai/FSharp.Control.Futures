namespace rec FSharp.Control.Futures.Sync.Channels

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync


[<Struct>]
type Reply<'a> =
    val private oneshot: OneShot<'a>

    private new(oneshot: OneShot<'a>) = { oneshot = oneshot }

    static member Create() : struct (Reply<'a> * Future<'a>) =
        let oneshot = OneShot<'a>()
        struct (Reply(oneshot), oneshot)

    static member Void() : Reply<'a> =
        Reply(Unchecked.defaultof<_>)

    member this.IsNeedsReply: bool =
        if isNull this.oneshot then
            false
        else
            not this.oneshot.IsClosed

    member this.Reply(reply: 'a): unit =
        if isNull this.oneshot then
            ()
        else
            this.oneshot.Send(reply) |> ignore
