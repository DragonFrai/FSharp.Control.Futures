[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures


type AsyncComputationBuilder with
    member _.For(source: IAsyncStreamer<'a>, action: 'a -> IAsyncComputation<unit>): IAsyncComputation<unit> =
        AsyncStreamer.iterAsync action source

type FutureBuilder with
    member _.For(source: Stream<'a>, action: 'a -> Future<unit>): Future<unit> =
        Future.create (fun () -> computation.For(Stream.run source, action >> Future.run))
