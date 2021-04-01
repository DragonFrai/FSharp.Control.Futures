namespace FSharp.Control.Futures.Streams

open System.ComponentModel
open FSharp.Control.Futures


[<Struct; RequireQualifiedAccess>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

[<RequireQualifiedAccess>]
module StreamPoll =
    val inline map: mapper: ('a -> 'b) -> poll: StreamPoll<'a> -> StreamPoll<'b>

[<Interface>]
type IStream<'a> =

    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract PollNext: Context -> StreamPoll<'a>

    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

module Stream =

    module Core =
        val inline create:
            __expand_pollNext: (Context -> StreamPoll<'a>) -> __expand_cancel: (unit -> unit) -> IStream<'a>
        val inline pollNext: context: Context -> stream: IStream<'a> -> StreamPoll<'a>
        val inline cancel: stream: IStream<'a> -> unit

    // --------
    // Creation
    // --------

    val empty<'a> : IStream<'a>

    val single: value: 'a -> IStream<'a>

    val always: value: 'a -> IStream<'a>

    val never: unit -> IStream<'a>

    val replicate: count: int -> value: 'a -> IStream<'a>

    val init: count: int -> initializer: (int -> 'a) -> IStream<'a>

    val initInfinite: initializer: (int -> 'a) -> IStream<'a>

    val ofSeq: src: 'a seq -> IStream<'a>

    // -----------
    // Compositors
    // -----------

    val map: mapper: ('a -> 'b) -> source: IStream<'a> -> IStream<'b>

    val collect: collector: ('a -> IStream<'b>) -> source: IStream<'a> -> IStream<'b>

    /// Alias to `PullStream.collect`
    val inline bind: binder: ('a -> IStream<'b>) -> source: IStream<'a> -> IStream<'b>

    val iter: action: ('a -> unit) -> source: IStream<'a> -> Future<unit>

    val iterAsync: action: ('a -> Future<unit>) -> source: IStream<'a> -> Future<unit>

    val fold: folder: ('s -> 'a -> 's) -> initState: 's -> source: IStream<'a> -> Future<'s>

    val scan: folder: ('s -> 'a -> 's) -> initState: 's -> source: IStream<'a> -> IStream<'s>

    val chooseV: chooser: ('a -> 'b voption) -> source: IStream<'a> -> IStream<'b>

    val tryPickV: chooser: ('a -> 'b voption) -> source: IStream<'a> -> Future<'b voption>

    val pickV: chooser: ('a -> 'b voption) -> source: IStream<'a> -> Future<'b>

    val join: source: IStream<IStream<'a>> -> IStream<'a>

    val append: source1: IStream<'a> -> source2: IStream<'a> -> IStream<'a>

    val filter: predicate: ('a -> bool) -> source: IStream<'a> -> IStream<'a>

    val any: predicate: ('a -> bool) -> source: IStream<'a> -> Future<bool>

    val all: predicate: ('a -> bool) -> source: IStream<'a> -> Future<bool>

    val zip: source1: IStream<'a> -> source2: IStream<'b> -> IStream<'a * 'b>

    val tryHeadV: source: IStream<'a> -> Future<'a voption>

    val tryLastV: source: IStream<'a> -> Future<'a voption>

    val ofFuture: fut: Future<'a> -> IStream<'a>

    /// Alias to `ofFuture`
    val inline singleAsync: x: Future<'a> -> IStream<'a>

    val delay: u2S: (unit -> IStream<'a>) -> IStream<'a>

    val take: count: int -> source: IStream<'a> -> IStream<'a>

    val bufferByCount: bufferSize: int -> source: IStream<'a> -> IStream<'a[]>
