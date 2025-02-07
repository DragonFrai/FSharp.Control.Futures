namespace FSharp.Control.Futures.Streams

open System
open FSharp.Control.Futures


/// <summary>
/// Like Poll&lt;Option&lt;'a&gt;&gt;, but optimized.
/// </summary>
type [<Struct; RequireQualifiedAccess>]
    PollNext<'a> =
    | Pending
    | Completed
    | Next of 'a
    | Transit of transitTo: IStream<'a>

/// <summary>
/// IAsyncEnumerator like type, optimized for processing multiple values in async sequence.
/// </summary>
/// <remarks>
/// Unlike IAsyncEnumerator, can be used only atomically in one Future,
/// because Drop call drop all Stream instead one element awaiter (and context can not be replaced).
/// </remarks>
//
// # Stream pollNext schema
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x1 ] ->
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x2 ] ->
// ...
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next xn ] ->
// [ StreamPoll.Pending -> ...(may be inf))... -> StreamPoll.Pending ] -> StreamPoll.Completed -> ... -> [ ! StreamCompletedException ]
//
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x1 ] ->
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x2 ] ->
// ...
// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next xn ] ->
// [ StreamPoll.Pending -> ...(may be inf))... -> StreamPoll.Pending ] -> StreamPoll.Transit next -> ... -> [ ! StreamCompletedException ]
//
// x1 != x2 != ... != xn
and [<Interface>]
    IStream<'a> =
    abstract PollNext: context: IContext -> PollNext<'a>
    abstract Drop: unit -> unit

type Stream<'a> = IStream<'a>


/// Exception is thrown when future is in a terminated state:
/// Ready, Polled with exception, Dropped
type StreamTerminatedException =
    inherit Exception
    new() = { inherit Exception() }
    new(message: string) = { inherit Exception(message) }


[<RequireQualifiedAccess>]
module Stream =
    let inline create (pollNext: IContext -> PollNext<'a>) (cancel: unit -> unit) =
        { new IStream<_> with
            member _.PollNext(ctx) = pollNext ctx
            member _.Drop() = cancel () }

    let inline cancel (stream: IStream<'a>) =
        stream.Drop()

    let inline pollNext (context: IContext) (stream: IStream<'a>) = stream.PollNext(context)

// module StreamPoll =
//
//     let inline map mapper poll =
//         match poll with
//         | StreamPoll.Next x -> StreamPoll.Next (mapper x)
//         | StreamPoll.Pending -> StreamPoll.Pending
//         | StreamPoll.Completed -> StreamPoll.Completed
//         | StreamPoll.Transit next -> Stream.Transit
//
//     let inline mapCompleted action poll =
//         match poll with
//         | StreamPoll.Completed -> action (); poll
//         | _ -> poll
//
//     let inline bindCompleted binder poll =
//         match poll with
//         | StreamPoll.Completed -> binder ()
//         | _ -> poll
