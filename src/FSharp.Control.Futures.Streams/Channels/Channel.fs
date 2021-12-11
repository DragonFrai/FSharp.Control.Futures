[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Channel

open FSharp.Control.Futures.Streams

type private SubjectChannel<'a>(sender: ISender<'a>, stream: Stream<'a>) =
    interface IChannel<'a> with
        member this.Cancel() = stream.Close()
        member this.PollNext(ctx) = stream.PollNext(ctx)
        member this.Dispose() = sender.Dispose()
        member this.Send(x) = sender.Send(x)

let ofPair (sender: ISender<'a>) (stream: Stream<'a>) : IChannel<'a> =
    upcast new SubjectChannel<'a>(sender, stream)

let inline asPair (ch: 'ch) : ISender<'a> * Stream<'a> when 'ch :> ISender<'a> and 'ch :> Stream<'a> =
    ch :> ISender<'a>, ch :> Stream<'a>
