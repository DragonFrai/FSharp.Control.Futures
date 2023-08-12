namespace FSharp.Control.Futures.Streams.Core

open FSharp.Control.Futures.Types


[<Struct; RequireQualifiedAccess>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

module StreamPoll =

    let inline map mapper poll =
        match poll with
        | StreamPoll.Next x -> StreamPoll.Next (mapper x)
        | StreamPoll.Pending -> StreamPoll.Pending
        | StreamPoll.Completed -> StreamPoll.Completed

    let inline mapCompleted action poll =
        match poll with
        | StreamPoll.Completed -> action (); poll
        | _ -> poll

    let inline bindCompleted binder poll =
        match poll with
        | StreamPoll.Completed -> binder ()
        | _ -> poll

// # SeqStream pollNext schema
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x1 ] ->
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x2 ] ->
// ...
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next xn ] ->
// [ StreamPoll.Pending -> ...(may be inf))... -> StreamPoll.Pending ] -> StreamPoll.Completed -> ... -> StreamPoll.Completed
//
// x1 != x2 != ... != xn
[<Interface>]
type IStream<'a> =
    abstract PollNext: IContext -> StreamPoll<'a>
    abstract Close: unit -> unit

type Stream<'a> = IStream<'a>

[<RequireQualifiedAccess>]
module Stream =
    let inline create (pollNext: IContext -> StreamPoll<'a>) (cancel: unit -> unit) =
        { new IStream<_> with
            member _.PollNext(ctx) = pollNext ctx
            member _.Close() = cancel () }

    let inline cancel (stream: IStream<'a>) =
        stream.Close()

    let inline pollNext (context: IContext) (stream: IStream<'a>) = stream.PollNext(context)
