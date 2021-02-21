[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures


type FutureBuilder with
    member _.For(source, action) =
        PullStream.iterAsync action source

