namespace rec FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


/// <summary>
/// Single Produces Single Consumer (SPSC) channel for only one msg.
/// OneShot used for sending single message between two Futures.
///
/// OneShot can be used only one Rx future and one Tx future,
/// Tx future must call only tx-methods, Rx future must call only rx-methods.
/// use OneShot in other cases is invalid.
///
/// If you need send value greater that one to one tasks, use other synchronisation primitives or channels.
/// </summary>
///
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
type OneShot<'a> =
    val mutable internal value: 'a
    val mutable internal notify: PrimaryNotify

    new(closed: bool) =
        { value = Unchecked.defaultof<'a>; notify = PrimaryNotify(false, closed) }

    new() =
        OneShot(false)

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

    member this.Close() : unit =
        do this.notify.Drop() |> ignore

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            if this.notify.Poll(ctx)
            then
                this.value <- Unchecked.defaultof<'a>
                Poll.Ready this.value
            else Poll.Pending

        member this.Drop() : unit =
            do this.notify.Drop() |> ignore


[<RequireQualifiedAccess>]
module OneShot =

    let create<'a> () : OneShot<'a> = OneShot()

    let inline send (msg: 'a) (oneshot: OneShot<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: OneShot<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: OneShot<'a>) : unit =
        oneshot.Close()

