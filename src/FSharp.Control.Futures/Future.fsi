namespace FSharp.Control.Futures

[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit

type Waker = unit -> unit

type IFuture<'a> =
    abstract member Poll: Waker -> Poll<'a>

type FuncFuture<'a> =
    new: poll: (Waker -> Poll<'a>) -> FuncFuture<'a>
    interface IFuture<'a>

module Future =

    val inline create: f: (Waker -> Poll<'a>) -> IFuture<'a>

    val inline poll: waker: Waker -> fut: IFuture<'a> -> Poll<'a>

    val ready: value: 'a -> IFuture<'a>

    val lazy': f: (unit -> 'a) -> IFuture<'a>

    val never: unit -> IFuture<'a>

    val bind: binder: ('a -> IFuture<'b>) -> fut: IFuture<'a> -> IFuture<'b>

    val map: mapping: ('a -> 'b) -> fut: IFuture<'a> -> IFuture<'b>

    val apply: f: IFuture<'a -> 'b> -> fut: IFuture<'a> -> IFuture<'b>

    val merge: fut1: IFuture<'a> -> fut2: IFuture<'b> -> IFuture<'a * 'b>

    val join: fut: IFuture<IFuture<'a>> -> IFuture<'a>

    val getWaker: IFuture<Waker>

    val ignore: future: IFuture<'a> -> IFuture<unit>

