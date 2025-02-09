namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


type IAddress<'i, 'o> =

    // [ Basis functions ]

    /// <summary>
    /// Sends the message and waits for it to be accepted
    /// (the sender will wait until the recipient's input queue is full).
    /// </summary>
    abstract SendMsg: Msg<'i, 'o> -> Future<unit>

    // /// <summary>
    // /// Send message but ignore queue capacity
    // /// </summary>
    // abstract PushMsg: Msg<'i, 'o> -> unit
    //
    // /// <summary>
    // /// Send message if message queue is not full.
    // /// </summary>
    // /// <returns>
    // /// - true if message was sent <br></br>
    // /// - false if message ignored
    // /// </returns>
    // abstract TrySendMsg: Msg<'i, 'o> -> bool

    // [ Derived functions ]

    /// <summary>
    /// Отправляет сообщение актору и ожидает ответа.
    /// Если получатель мертв и не обрабатывает сообщения, выкинет исключение.
    /// </summary>
    abstract Send: 'i -> Future<'o>

    /// <summary>
    /// Отправляет сообщение актору и ожидает ответа.
    /// Если получатель мертв и не обрабатывает сообщения, вернет ошибку.
    /// </summary>
    // abstract TrySend: 'i -> Future<Result<'o, SendError>>

    // /// <summary>
    // /// Мгновенно отправляет сообщение актору, но не может дождаться его ответа.
    // /// Если очередь актора заполнена или он уже мертв, возвращает ошибку.
    // /// </summary>
    // abstract TryPush: 'i -> Result<unit, TryPushError>
    //
    // /// <summary>
    // /// Отправляет сообщение актору игнорируя размер его очереди и другие ошибки.
    // /// Возвращает ошибку если актор уже умер.
    // /// </summary>
    // abstract Push: 'i -> Result<unit, PushError>

[<AbstractClass>]
type BaseAddress<'i, 'o>() =

    abstract Post: Msg<'i, 'o> -> Future<unit>

    interface IAddress<'i, 'o> with

        member this.SendMsg(msg) =
            this.Post(msg)

        member this.Send(msg) = future {
            let os = OneShot<'o>.Create()
            let msg = Msg(msg, os.AsTx)
            do! this.Post(msg)
            return! os.Await()
        }
