namespace rec FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


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
///         os.Send(12)
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

    member inline this.AsSender: OneShotSender<'a> = OneShotSender(this)
    member inline this.AsReceiver: OneShotReceiver<'a> = OneShotReceiver(this)
    member inline this.AsPair: OneShotSender<'a> * OneShotReceiver<'a> = this.AsSender, this.AsReceiver

    member this.IsClosed: bool = this.notify.IsTerminated

    member inline internal this.SendResult(result: 'a): bool =
        if this.notify.IsNotified then invalidOp "OneShot already contains value"
        this.value <- result
        let isSuccess = this.notify.Notify()
        if not isSuccess then
            this.value <- Unchecked.defaultof<_>
        isSuccess

    /// <summary>
    /// Send msg to receiver and return true.
    /// If Receiver closed, return false
    /// </summary>
    /// <param name="msg"></param>
    member this.Send(msg: 'a): bool =
        this.SendResult(msg)

    /// <summary>
    /// Close receiving. This method can prevent sending from sender.
    /// </summary>
    member this.Close() : unit =
        do this.notify.Drop() |> ignore

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            if this.notify.Poll(ctx)
            then
                let value = this.value
                this.value <- Unchecked.defaultof<'a>
                Poll.Ready value
            else Poll.Pending

        member this.Drop() : unit =
            do this.notify.Drop() |> ignore

[<Struct>]
type OneShotSender<'a> =
    val private oneshot: OneShot<'a>
    new(os: OneShot<'a>) = { oneshot = os }

    member this.IsClosed: bool = this.oneshot.IsClosed
    member this.Send(msg): bool = this.oneshot.Send(msg)

[<Struct>]
type OneShotReceiver<'a> =
    val private oneshot: OneShot<'a>
    new(os: OneShot<'a>) = { oneshot = os }

    member this.Receive(): Future<'a> = this.oneshot
    member this.Close(): unit = this.oneshot.Close()


[<RequireQualifiedAccess>]
module OneShot =

    let create<'a> () : OneShot<'a> = OneShot()

    let inline send (msg: 'a) (oneshot: OneShot<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: OneShot<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: OneShot<'a>) : unit =
        oneshot.Close()

