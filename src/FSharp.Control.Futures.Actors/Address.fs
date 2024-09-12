namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


type IAddress<'i, 'o> =

    // TODO: Add immediate not async version
    abstract Post: Msg<'i, 'o> -> Future<unit>

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

        member this.Post(msg) =
            this.Post(msg)

        member this.Send(msg) =
            Future.delay (fun () ->
                let os = OneShot<'o>()
                let msg = Msg(msg, os.AsSink)
                this.Post(msg)
                os.AsVar)
