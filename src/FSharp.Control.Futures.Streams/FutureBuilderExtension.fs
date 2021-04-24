[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures


type FutureBuilder with
    member _.For(source: IStream<'a>, action: 'a -> IComputationTmp<unit>): IComputationTmp<unit> =
        Stream.iterAsync action source
