[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures


type FutureBuilder with
    // TODO?: Replace by ForBang syntax
    // for! x in source do <action>
    member _.For(source: IPullStream<'a>, action: 'a -> Future<unit>): Future<unit> =
        PullStream.iterAsync action source
