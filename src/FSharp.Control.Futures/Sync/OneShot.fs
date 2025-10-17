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
///         os.AsSink.Send(12)
///         return ()
///     })
///
///     let! x = os.Await()
///     do printfn $"> {x}"
/// }
///
///
/// ```
/// </example>
[<Class>]
[<Sealed>]
type OneShot<'a> =

    val mutable private value: 'a
    val mutable private notify: PrimaryNotify

    private new(closed: bool) =
        { value = Unchecked.defaultof<'a>
          notify = PrimaryNotify(false, closed) }

    new() = OneShot(false)
    static member Closed: OneShot<'a> = OneShot(true)

    member inline this.AsSend: OneSend<'a> = OneSend(this)
    member inline this.AsReceive: OneReceive<'a> = OneReceive(this)
    member inline this.AsPair: OneSend<'a> * OneReceive<'a> = OneSend(this), OneReceive(this)

    /// <summary>
    /// Проверяет закрыт ли OneShot.
    /// </summary>
    member this.IsClosed: bool = this.notify.IsTerminated

    /// <summary>
    /// Закрывает получение значения.
    /// </summary>
    /// <remarks>
    /// Future возвращенная вызовом <c>Receive()</c> будет завершаться исключением после закрытия.
    /// Поэтому если <c>Receive()</c> уже был вызван, предпочтительным способом отмены ожидания будет использование
    /// <c> rxFuture.Drop() </c> вместо прямой отмены.
    /// Этого можно добиться используя её комбинацию с Future определяющей условие отмены.
    /// Например:
    /// <code>
    /// future {
    ///     let tx, rx = OneShot.createPair ()
    ///     let _fTask = ThreadPoolScheduler.spawn (createSenderFuture tx)
    ///     let! valueWithTimeout =
    ///         Future.first (Future.map Ok rx.Receive()) (Future.sleepMs 1000 |> Future.map (fun () -> Error "timeout"))
    /// }
    /// </code>
    /// </remarks>
    member this.Close() : unit = do this.notify.Drop() |> ignore

    /// <summary>
    /// Начинает асинхронное ожидание.
    /// </summary>
    /// <remarks>
    /// Может быть вызван только один раз.
    /// </remarks>
    /// <remarks>
    /// Вызов <c>Drop</c> возвращенной Future приведет к закрытию (как вызов <c>Close</c>).
    /// </remarks>
    /// <remarks>
    /// OneShot сам является Future и может быть использован напрямую.
    /// </remarks>
    member this.Receive() : Future<'a> = this

    /// <summary>
    /// Отправляет значение приемнику.
    /// </summary>
    /// <param name="msg"> Передаваемое значение </param>
    /// <returns>
    /// true, если сообщение было успешно отправлено и false, если OneShot уже был закрыт.
    /// </returns>
    member this.Send(msg: 'a) : bool = // TODO: Semantically result
        if this.notify.IsNotified then invalidOp "OneShot already contains value or closed"
        this.value <- msg
        let isSuccess = this.notify.Notify()
        if not isSuccess then
            this.value <- Unchecked.defaultof<_>
        isSuccess

    member this.DoSend(msg: 'a) : unit =
        this.Send(msg) |> ignore

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


/// <summary>
/// Приемник одного асинхронного значения.
/// Может быть преобразован в Future путем вызова <c> rx.Receive() </c>
/// </summary>
/// <remarks>
/// Receive может быть вызван только один раз.
/// </remarks>
[<Struct; NoComparison; NoEquality>]
type OneReceive<'a> =
    val Inner: OneShot<'a>
    new(oneshot: OneShot<'a>) = { Inner = oneshot }
    member this.IsClosed: bool = this.Inner.IsClosed
    member this.Receive(): Future<'a> = this.Inner.Receive()
    member this.Close(): unit = this.Inner.Close()

/// <summary>
/// Отправитель одного асинхронного значения.
/// </summary>
[<Struct; NoComparison; NoEquality>]
type OneSend<'a> =
    val Inner: OneShot<'a>
    new(oneshot: OneShot<'a>) = { Inner = oneshot }
    member this.IsClosed: bool = this.Inner.IsClosed
    member this.Send(msg: 'a): bool = this.Inner.Send(msg)
    member this.DoSend(msg: 'a): unit = this.Inner.DoSend(msg)

[<RequireQualifiedAccess>]
module OneShot =

    let inline create<'a> () : OneShot<'a> = OneShot()
    let inline createPair<'a> () : OneSend<'a> * OneReceive<'a> = (create ()).AsPair

    let inline closed<'a> : OneShot<'a> = OneShot<'a>.Closed
    let inline closedPair<'a> : OneSend<'a> * OneReceive<'a> = OneShot<'a>.Closed.AsPair

    let inline send (msg: 'a) (oneshot: OneShot<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: OneShot<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: OneShot<'a>) : unit =
        oneshot.Close()

    let inline receive (oneshot: OneShot<'a>) : Future<'a> =
        oneshot.Receive()


[<RequireQualifiedAccess>]
module OneSend =
    let inline send (msg: 'a) (oneshot: OneSend<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: OneSend<'a>) : bool =
        oneshot.IsClosed


[<RequireQualifiedAccess>]
module OneReceive =

    let inline isClosed (oneshot: OneReceive<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: OneReceive<'a>) : unit =
        oneshot.Close()

    let inline receive (oneshot: OneReceive<'a>) : Future<'a> =
        oneshot.Receive()
