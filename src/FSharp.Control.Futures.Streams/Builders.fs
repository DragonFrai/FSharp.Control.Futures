namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures

type StreamBuilder() =

    member inline _.Yield(x) = Stream.single x

    member inline _.YieldFrom(x: Stream<'a>): Stream<'a> = x

    member inline _.YieldFrom(x: IFuture<'a>): Stream<'a> = Stream.ofComputation x

    member inline _.YieldFrom(xs: 'a seq): Stream<'a> = Stream.ofSeq xs

    member inline _.Zero() = Stream.empty

    member inline _.Bind(x: Stream<'a>, f: 'a -> Stream<'b>): Stream<'b> = Stream.collect f x

    member inline this.Bind(x: IFuture<'a>, f: 'a -> Stream<'b>): Stream<'b> = this.Bind(Stream.ofComputation x, f)

    member inline _.Combine(s1: Stream<'a>, u2S2: unit -> Stream<'a>): Stream<'a> = Stream.append s1 (Stream.delay u2S2)

    member inline this.Combine(uS: Stream<unit>, u2aS: unit -> Stream<'a>): Stream<'a> = this.Bind(uS, u2aS)

    member inline _.Delay(f: unit -> Stream<'a>): unit -> Stream<'a> = f

    member inline _.MergeSources(s1: Stream<'a>, s2: Stream<'b>): Stream<'a * 'b> = Stream.zip s1 s2

    member inline _.Run(u2S: unit -> Stream<'a>): Stream<'a> = Stream.delay u2S

    member inline _.For(source, body) = Stream.collect body source

    member inline this.For(source, body) = this.For(Stream.ofSeq source, body)

    member inline this.While(cond: unit -> bool, body: unit -> Stream<'a>): Stream<'a> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

[<AutoOpen>]
module StreamBuilderImpl =
    let stream = StreamBuilder()
