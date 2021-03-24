namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


type StreamBuilder() =

    member _.Yield(x) = Stream.single x

    member _.YieldFrom(x: IStream<'a>): IStream<'a> = x

    member _.YieldFrom(x: Future<'a>): IStream<'a> = Stream.ofFuture x

    member _.YieldFrom(xs: 'a seq): IStream<'a> = Stream.ofSeq xs

    member _.Zero() = Stream.empty ()

    member _.Bind(x: IStream<'a>, f: 'a -> IStream<'b>): IStream<'b> = Stream.collect f x

    member this.Bind(x: Future<'a>, f: 'a -> IStream<'b>): IStream<'b> = this.Bind(Stream.ofFuture x, f)

    member _.Combine(s1: IStream<'a>, u2S2: unit -> IStream<'a>): IStream<'a> = Stream.append s1 (Stream.delay u2S2)

    member this.Combine(uS: IStream<unit>, u2aS: unit -> IStream<'a>): IStream<'a> = this.Bind(uS, u2aS)

    member _.Delay(f: unit -> IStream<'a>): unit -> IStream<'a> = f

    member _.MergeSources(s1: IStream<'a>, s2: IStream<'b>): IStream<'a * 'b> = Stream.zip s1 s2

    member _.Run(u2S: unit -> IStream<'a>): IStream<'a> = Stream.delay u2S

    member this.While(cond: unit -> bool, body: unit -> IStream<'a>): IStream<'a> =
        let rec loop (): IStream<'a> =
            if cond ()
            then this.Combine(body (), loop)
            else Stream.empty ()
        loop ()

[<AutoOpen>]
module StreamBuilderImpl =
    let stream = StreamBuilder()
