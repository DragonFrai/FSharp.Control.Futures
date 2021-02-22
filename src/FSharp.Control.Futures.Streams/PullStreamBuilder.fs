namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


type PullStreamBuilder() =

    member _.Yield(x) = PullStream.single x

    member _.YieldFrom(x: IPullStream<'a>): IPullStream<'a> = x

    member _.YieldFrom(x: Future<'a>): IPullStream<'a> = PullStream.ofFuture x

    member _.YieldFrom(xs: 'a seq): IPullStream<'a> = PullStream.ofSeq xs

    member _.Zero() = PullStream.empty ()

    member _.Bind(x: IPullStream<'a>, f: 'a -> IPullStream<'b>): IPullStream<'b> = PullStream.collect f x

    member this.Bind(x: Future<'a>, f: 'a -> IPullStream<'b>): IPullStream<'b> = this.Bind(PullStream.ofFuture x, f)

    member _.Combine(s1: IPullStream<'a>, u2S2: unit -> IPullStream<'a>): IPullStream<'a> = PullStream.append s1 (PullStream.delay u2S2)

    member this.Combine(uS: IPullStream<unit>, u2aS: unit -> IPullStream<'a>): IPullStream<'a> = this.Bind(uS, u2aS)

    member _.Delay(f: unit -> IPullStream<'a>): unit -> IPullStream<'a> = f

    member _.MergeSources(s1: IPullStream<'a>, s2: IPullStream<'b>): IPullStream<'a * 'b> = PullStream.zip s1 s2

    member _.Run(u2S: unit -> IPullStream<'a>): IPullStream<'a> = PullStream.delay u2S


module PullStreamBuilderImpl =
    let pullStream = PullStreamBuilder()

//    let x =
//        pullStream {
//            yield 1
//            yield! [ 1; 2 ]
//            do! Future.sleep 1000
//            yield 2
//            let! x = Future.ready 4
//            yield x
//        }

