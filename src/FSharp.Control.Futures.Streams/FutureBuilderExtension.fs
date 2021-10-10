[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures.Core
open FSharp.Control.Futures


type FutureBuilder with
    member _.For(source: IAsyncStreamer<'a>, action: 'a -> IFuture<unit>): IFuture<unit> =
        AsyncStreamer.iterAsync action source

type FutureBuilder_OLD with
    member _.For(source: Stream<'a>, action: 'a -> Future<unit>): Future<unit> =
        Future_OLD.create (fun () -> computation.For(Stream.runStreaming source, action >> Future.startComputation))
