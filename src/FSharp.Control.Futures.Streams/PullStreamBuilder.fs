namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


type PullStreamBuilder() =

    member _.Yield(x) = PullStream.single x

    member _.YieldFrom(x: IPullStream<'a>): IPullStream<'a> = x

    member _.YieldFrom(x: Future<'a>): IPullStream<'a> = notImplemented "" // TODO: Implement

    member _.Zero() = PullStream.empty ()

    member _.Bind(x: IPullStream<'a>, f: 'a -> IPullStream<'a>) = PullStream.collect f x

    member _.Bind(x: Future<'a>, f: 'a -> IPullStream<'a>): IPullStream<'a> = notImplemented "" // TODO: Implement

    member _.Combine(s1: IPullStream<'a>, s2: IPullStream<'a>): IPullStream<'a> = PullStream.append s1 s2

    member _.Delay(f: unit -> IPullStream<'a>): IPullStream<'a> = notImplemented "" // TODO: Implement

    member _.MergeSources(s1: IPullStream<'a>, s2: IPullStream<'b>): IPullStream<'a * 'b> = PullStream.zip s1 s2


module PullStreamBuilderImpl =
    let pullStream = PullStreamBuilder()
