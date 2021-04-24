namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


type StreamBuilder() =

    member inline _.Yield(x) = Stream.single x

    member inline _.YieldFrom(x: IStream<'a>): IStream<'a> = x

    member inline _.YieldFrom(x: IComputationTmp<'a>): IStream<'a> = Stream.ofFuture x

    member inline _.YieldFrom(xs: 'a seq): IStream<'a> = Stream.ofSeq xs

    member inline _.Zero() = Stream.empty

    member inline _.Bind(x: IStream<'a>, f: 'a -> IStream<'b>): IStream<'b> = Stream.collect f x

    member inline this.Bind(x: IComputationTmp<'a>, f: 'a -> IStream<'b>): IStream<'b> = this.Bind(Stream.ofFuture x, f)

    member inline _.Combine(s1: IStream<'a>, u2S2: unit -> IStream<'a>): IStream<'a> = Stream.append s1 (Stream.delay u2S2)

    member inline this.Combine(uS: IStream<unit>, u2aS: unit -> IStream<'a>): IStream<'a> = this.Bind(uS, u2aS)

    member inline _.Delay(f: unit -> IStream<'a>): unit -> IStream<'a> = f

    member inline _.MergeSources(s1: IStream<'a>, s2: IStream<'b>): IStream<'a * 'b> = Stream.zip s1 s2

    member inline _.Run(u2S: unit -> IStream<'a>): IStream<'a> = Stream.delay u2S

    member inline _.For(source, body) = Stream.collect body source

    member inline this.For(source, body) = this.For(Stream.ofSeq source, body)

    member inline this.While(cond: unit -> bool, body: unit -> IStream<'a>): IStream<'a> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

[<AutoOpen>]
module StreamBuilderImpl =
    let stream = StreamBuilder()
