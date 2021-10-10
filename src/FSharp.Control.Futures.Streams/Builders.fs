namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures.Core
open FSharp.Control.Futures


type AsyncStreamerBuilder() =

    member inline _.Yield(x) = AsyncStreamer.single x

    member inline _.YieldFrom(x: IAsyncStreamer<'a>): IAsyncStreamer<'a> = x

    member inline _.YieldFrom(x: IFuture<'a>): IAsyncStreamer<'a> = AsyncStreamer.ofComputation x

    member inline _.YieldFrom(xs: 'a seq): IAsyncStreamer<'a> = AsyncStreamer.ofSeq xs

    member inline _.Zero() = AsyncStreamer.empty

    member inline _.Bind(x: IAsyncStreamer<'a>, f: 'a -> IAsyncStreamer<'b>): IAsyncStreamer<'b> = AsyncStreamer.collect f x

    member inline this.Bind(x: IFuture<'a>, f: 'a -> IAsyncStreamer<'b>): IAsyncStreamer<'b> = this.Bind(AsyncStreamer.ofComputation x, f)

    member inline _.Combine(s1: IAsyncStreamer<'a>, u2S2: unit -> IAsyncStreamer<'a>): IAsyncStreamer<'a> = AsyncStreamer.append s1 (AsyncStreamer.delay u2S2)

    member inline this.Combine(uS: IAsyncStreamer<unit>, u2aS: unit -> IAsyncStreamer<'a>): IAsyncStreamer<'a> = this.Bind(uS, u2aS)

    member inline _.Delay(f: unit -> IAsyncStreamer<'a>): unit -> IAsyncStreamer<'a> = f

    member inline _.MergeSources(s1: IAsyncStreamer<'a>, s2: IAsyncStreamer<'b>): IAsyncStreamer<'a * 'b> = AsyncStreamer.zip s1 s2

    member inline _.Run(u2S: unit -> IAsyncStreamer<'a>): IAsyncStreamer<'a> = AsyncStreamer.delay u2S

    member inline _.For(source, body) = AsyncStreamer.collect body source

    member inline this.For(source, body) = this.For(AsyncStreamer.ofSeq source, body)

    member inline this.While(cond: unit -> bool, body: unit -> IAsyncStreamer<'a>): IAsyncStreamer<'a> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

[<AutoOpen>]
module AsyncStreamerBuilderImpl =
    let streamer = AsyncStreamerBuilder()

type StreamBuilder() =

    member inline _.Yield(x: 'a): Stream<'a> =
        Stream.create (fun () -> streamer.Yield(x))

    member inline _.YieldFrom(x: Stream<'a>): Stream<'a> = x

    member inline _.YieldFrom(x: Future<'a>): Stream<'a> = Stream.singleAsync x

    member inline _.YieldFrom(xs: 'a seq): Stream<'a> = Stream.ofSeq xs

    member inline _.Zero() = Stream.empty

    member inline _.Bind(x: Stream<'a>, f: 'a -> Stream<'b>): Stream<'b> = Stream.collect f x

    member inline this.Bind(x: Future<'a>, f: 'a -> Stream<'b>): Stream<'b> = this.Bind(Stream.singleAsync x, f)

    member inline _.Combine(s1: Stream<'a>, u2s: unit -> Stream<'a>): Stream<'a> =
        Stream.append s1 (Stream.create (u2s >> Stream.runStreaming))

    member inline this.Combine(uS: Stream<unit>, u2s: unit -> Stream<'a>): Stream<'a> = this.Bind(uS, u2s)

    member inline _.Delay(f: unit -> Stream<'a>): unit -> Stream<'a> = f

    member inline _.MergeSources(s1: Stream<'a>, s2: Stream<'b>): Stream<'a * 'b> = Stream.zip s1 s2

    member inline _.Run(u2s: unit -> Stream<'a>): Stream<'a> = Stream.create (u2s >> Stream.runStreaming)

    member inline _.For(source: Stream<'a>, body: 'a -> Stream<unit>) : Stream<unit> = Stream.collect body source

    member inline this.For(source, body) = this.For(Stream.ofSeq source, body)

    member inline this.While(cond: unit -> bool, body: unit -> Stream<unit>): Stream<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)


[<AutoOpen>]
module StreamBuilderImpl =
    let stream = StreamBuilder()


