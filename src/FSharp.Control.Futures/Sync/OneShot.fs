namespace rec FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


/// <summary>
/// Приемник одного асинхронного значения.
/// Может быть преобразован в Future путем вызова <c> rx.Await() </c>
/// </summary>
[<Interface>]
type IOneShotRx<'a> =

    /// <summary>
    /// Проверяет закрыт ли OneShot.
    /// </summary>
    abstract IsClosed: bool

    /// <summary>
    /// Начинает асинхронное ожидание.
    /// </summary>
    /// <remarks>
    /// Может быть вызван только один раз.
    /// </remarks>
    /// <remarks>
    /// Вызов <c>Drop</c> возвращенной Future приведет к закрытию (как вызов <c>Close</c>).
    /// </remarks>
    abstract Await: unit -> Future<'a>

    /// <summary>
    /// Закрывает получение значения.
    /// </summary>
    /// <remarks>
    /// Future возвращенная вызовом <c>Await()</c> будет завершаться исключением после закрытия.
    /// Поэтому если <c>Await()</c> уже был вызван, предпочтительным способом отмены ожидания будет использование
    /// <c> rxFuture.Drop() </c> вместо прямой отмены.
    /// Этого можно добиться используя её компибацию с Future определяющей условие отмены.
    /// Например:
    /// <code>
    /// future {
    ///     let tx, rx = OnoShot.createTxRx ()
    ///     let _fTask = ThreadPoolScheduler.spawn (createSenderFuture tx)
    ///     let! valueWithTimeout =
    ///         Future.first (Future.map Ok rx.Await()) (Future.sleepMs 1000 |> Future.map (fun () -> Error "timeout"))
    /// }
    /// </code>
    /// </remarks>
    abstract Close: unit -> unit

/// <summary>
/// Отправитель одного асинхронного значения.
/// </summary>
[<Interface>]
type IOneShotTx<'a> =

    /// <summary>
    /// Проверяет закрыт ли OneShot.
    /// </summary>
    abstract IsClosed: bool

    /// <summary>
    /// Отправляет значение приемнику.
    /// </summary>
    /// <param name="msg"> Передаваемое значение </param>
    /// <returns>
    /// true, если сообщение было успешно отправлено и false, если OneShot уже был закрыт.
    /// </returns>
    abstract Send: msg: 'a -> bool

[<AutoOpen>]
module IOneShotTxExtensions =
    type IOneShotTx<'a> with
        member inline this.DoSend(msg: 'a): unit =
            this.Send(msg) |> ignore

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
[<AbstractClass>]
type OneShot<'a> internal () =

    // TODO?: Add AggressiveInlining + InternalCall

    static member Create(): OneShot<'a> = OneShotImpl()
    static member Create(closed: bool): OneShot<'a> = OneShotImpl(closed)

    abstract IsClosed: bool
    abstract Send: msg: 'a -> bool
    abstract Close: unit -> unit
    abstract Await: unit -> Future<'a>

    member inline this.AsTx: IOneShotTx<'a> = this
    member inline this.AsRx: IOneShotRx<'a> = this
    member inline this.AsTxRx: IOneShotTx<'a> * IOneShotRx<'a> = this, this

    interface IOneShotTx<'a> with
        member this.IsClosed = this.IsClosed
        member this.Send(msg) = this.Send(msg)

    interface IOneShotRx<'a> with
        member this.IsClosed = this.IsClosed
        member this.Await() = this.Await()
        member this.Close() = this.Close()

[<Class>]
[<Sealed>]
type internal OneShotImpl<'a> =
    inherit OneShot<'a>

    val mutable internal value: 'a
    val mutable internal notify: PrimaryNotify

    new(closed: bool) =
        { value = Unchecked.defaultof<'a>
          notify = PrimaryNotify(false, closed) }

    new() =
        OneShotImpl(false)

    member inline internal this.SendResult(result: 'a): bool =
        if this.notify.IsNotified then invalidOp "OneShot already contains value"
        this.value <- result
        let isSuccess = this.notify.Notify()
        if not isSuccess then
            this.value <- Unchecked.defaultof<_>
        isSuccess

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

    // [ Impl OneShot base class ]
    override this.IsClosed: bool = this.notify.IsTerminated
    override this.Send(msg: 'a): bool = this.SendResult(msg)
    override this.Close() : unit = do this.notify.Drop() |> ignore
    override this.Await() : Future<'a> = this


[<RequireQualifiedAccess>]
module OneShot =

    let inline create<'a> () : OneShot<'a> = OneShot.Create()
    let inline closed<'a> () : OneShot<'a> = OneShot.Create(true)

    let inline createTxRx<'a> () : IOneShotTx<'a> * IOneShotRx<'a> = (create ()).AsTxRx
    let inline closedTxRx<'a> () : IOneShotTx<'a> * IOneShotRx<'a> = (closed ()).AsTxRx

    let inline send (msg: 'a) (oneshot: IOneShotTx<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: IOneShotTx<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: IOneShotRx<'a>) : unit =
        oneshot.Close()

    let inline await (oneshot: IOneShotRx<'a>) : Future<'a> =
        oneshot.Await()
