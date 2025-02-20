namespace rec FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


/// <summary>
/// Приемник одного асинхронного значения.
/// Может быть преобразован в Future путем вызова <c> rx.Await() </c>
/// </summary>
/// <remarks>
/// Await может быть вызван только один раз.
/// </remarks>
[<Struct; NoComparison; NoEquality>]
type OneShotRx<'a> internal (impl: OneShotImpl<'a>) =
    member this.IsClosed: bool = impl.IsClosed
    member this.Await(): Future<'a> = impl.Await()
    member this.Close(): unit = impl.Close()

/// <summary>
/// Отправитель одного асинхронного значения.
/// </summary>
[<Struct; NoComparison; NoEquality>]
type OneShotTx<'a> internal (impl: OneShotImpl<'a>) =
    member this.IsClosed: bool = impl.IsClosed
    member this.Send(msg: 'a): bool = impl.Send(msg)


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
[<Struct; NoComparison; NoEquality>]
type OneShot<'a> internal (impl: OneShotImpl<'a>) =

    static member Create(): OneShot<'a> = OneShot(OneShotImpl())
    static member Closed(): OneShot<'a> = OneShot(OneShotImpl(true))

    /// <summary>
    /// Проверяет закрыт ли OneShot.
    /// </summary>
    member this.IsClosed: bool = impl.IsClosed

    /// <summary>
    /// Отправляет значение приемнику.
    /// </summary>
    /// <param name="msg"> Передаваемое значение </param>
    /// <returns>
    /// true, если сообщение было успешно отправлено и false, если OneShot уже был закрыт.
    /// </returns>
    member this.Send(msg: 'a): bool = impl.Send(msg)

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
    member this.Close(): unit = impl.Close()

    /// <summary>
    /// Начинает асинхронное ожидание.
    /// </summary>
    /// <remarks>
    /// Может быть вызван только один раз.
    /// </remarks>
    /// <remarks>
    /// Вызов <c>Drop</c> возвращенной Future приведет к закрытию (как вызов <c>Close</c>).
    /// </remarks>
    member this.Await(): Future<'a> = impl.Await()

    member this.AsTx: OneShotTx<'a> = OneShotTx(impl)
    member this.AsRx: OneShotRx<'a> = OneShotRx(impl)
    member this.AsTxRx: OneShotTx<'a> * OneShotRx<'a> = OneShotTx(impl), OneShotRx(impl)


[<AutoOpen>]
module OneShotTxExtensions =
    type OneShotTx<'a> with
        member inline this.Send(msg: 'a): unit =
            this.Send(msg) |> ignore


[<RequireQualifiedAccess>]
module OneShot =

    let inline create<'a> () : OneShot<'a> = OneShot.Create()
    let inline closed<'a> () : OneShot<'a> = OneShot.Closed()

    let inline createTxRx<'a> () : OneShotTx<'a> * OneShotRx<'a> = (create ()).AsTxRx
    let inline closedTxRx<'a> () : OneShotTx<'a> * OneShotRx<'a> = (closed ()).AsTxRx

    let inline send (msg: 'a) (oneshot: OneShot<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: OneShot<'a>) : bool =
        oneshot.IsClosed

    let inline close (oneshot: OneShot<'a>) : unit =
        oneshot.Close()

    let inline await (oneshot: OneShot<'a>) : Future<'a> =
        oneshot.Await()


[<RequireQualifiedAccess>]
module OneShotTx =
    let inline send (msg: 'a) (oneshot: OneShotTx<'a>) : bool =
        oneshot.Send(msg)

    let inline isClosed (oneshot: OneShotTx<'a>) : bool =
        oneshot.IsClosed


[<RequireQualifiedAccess>]
module OneShotRx =
    let inline close (oneshot: OneShotRx<'a>) : unit =
        oneshot.Close()

    let inline await (oneshot: OneShotRx<'a>) : Future<'a> =
        oneshot.Await()
