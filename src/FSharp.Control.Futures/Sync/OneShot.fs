namespace rec FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


[<Interface>]
type IOneShotVar<'a> =
    inherit IFuture<'a>
    abstract Close: unit -> unit

[<Interface>]
type IOneShotSink<'a> =
    abstract IsClosed: bool
    abstract Send: msg: 'a -> bool

/// <summary>
/// Single Produces Single Consumer (SPSC) channel for only one msg.
/// OneShot used for sending single message between two Futures.
///
/// OneShot can be used only in one Rx future and one Tx future,
/// Tx future must call only tx-methods, Rx future must call only rx-methods.
/// use OneShot in other cases is invalid.
///
/// If you need send value greater that one to one tasks, use other synchronisation primitives or channels.
/// </summary>
/// <example>
/// ```fsharp
/// future {
///     let os = OneShot()
///     ThreadPoolRuntime.spawn (future {
///         do! Future.sleepMs 1000
///         os.AsSink.Send(12)
///         return ()
///     })
///
///     let! x = os
///     do printfn $"> {x}"
/// }
///
///
/// ```
/// </example>
[<Class>]
[<Sealed>]
type OneShot<'a> =
    val mutable internal value: 'a
    val mutable internal notify: PrimaryNotify

    new(closed: bool) =
        { value = Unchecked.defaultof<'a>; notify = PrimaryNotify(false, closed) }

    new() =
        OneShot(false)

    member inline this.AsSink: IOneShotSink<'a> = this
    member inline this.AsVar: IOneShotVar<'a> = this
    member inline this.AsPair: IOneShotSink<'a> * IOneShotVar<'a> = this.AsSink, this.AsVar

    member inline internal this.SendResult(result: 'a): bool =
        if this.notify.IsNotified then invalidOp "OneShot already contains value"
        this.value <- result
        let isSuccess = this.notify.Notify()
        if not isSuccess then
            this.value <- Unchecked.defaultof<_>
        isSuccess

    interface IOneShotSink<'a> with

        member this.IsClosed: bool =
            this.notify.IsTerminated

        /// <summary>
        /// Send msg to receiver and return true.
        /// If Receiver closed, return false
        /// </summary>
        /// <param name="msg"></param>
        member this.Send(msg: 'a): bool =
            this.SendResult(msg)

    interface IOneShotVar<'a> with
        /// <summary>
        /// Close receiving. This method can prevent sending from sender.
        /// </summary>
        member this.Close() : unit =
            do this.notify.Drop() |> ignore

        member this.Poll(ctx: IContext) : Poll<'a> =
            if this.notify.Poll(ctx)
            then
                let value = this.value
                this.value <- Unchecked.defaultof<'a>
                Poll.Ready value
            else Poll.Pending

        member this.Drop() : unit =
            do this.notify.Drop() |> ignore

[<RequireQualifiedAccess>]
module OneShot =

    let create<'a> () : OneShot<'a> = OneShot()

    let inline send (msg: 'a) (oneshot: IOneShotSink<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: IOneShotSink<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: IOneShotVar<'a>) : unit =
        oneshot.Close()

    // let inline await (oneshot: IOneShotVar<'a>) : Future<'a> =
    //     oneshot
