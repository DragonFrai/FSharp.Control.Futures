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
/// <example>
/// ```fsharp
/// let os = OneShot()
///
///
/// ```
/// </example>
[<Class>]
type OneShot<'a> =
    val mutable internal value: ExnResult<'a>
    val mutable internal notify: PrimaryNotify

    new () =
        { value = ExnResult.Uninit()
          notify = PrimaryNotify(false) }

    member this.IsClosed: bool = this.notify.IsTerminated

    member inline internal this.SendResult(result: ExnResult<'a>): bool =
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
        this.SendResult(ExnResult.Ok(msg))

    /// <summary>
    /// Send exception to receiver (receiver throw exception when receive) and return true.
    /// If Receiver closed, return false
    /// </summary>
    /// <param name="msg"></param>
    member this.SendExn(ex: exn): bool =
        this.SendResult(ExnResult.Exn(ex))

    member this.Close() : unit =
        do this.notify.Drop() |> ignore

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            if this.notify.Poll(ctx)
            then Poll.Ready this.value.Value
            else Poll.Pending

        member this.Drop() : unit =
            do this.notify.Drop() |> ignore


[<RequireQualifiedAccess>]
module OneShot =

    let create<'a> () : OneShot<'a> = OneShot()

    let inline send (msg: 'a) (oneshot: OneShot<'a>) : bool =
        oneshot.Send(msg)

    let inline sendExn (ex: exn) (oneshot: OneShot<'a>) : bool =
        oneshot.SendExn(ex)

    let inline isClosed (oneshot: OneShot<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: OneShot<'a>) : unit =
        oneshot.Close()

