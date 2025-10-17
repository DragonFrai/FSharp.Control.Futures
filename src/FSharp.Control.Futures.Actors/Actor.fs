namespace FSharp.Control.Futures.Actors

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Actors.Addressing


// # Жизненный цикл актора
//
// 0. Inactive --
//    Актор только создан, но не запущен
// 1. Starting --
//    Процедура запуска актора.
//    Еще не может полноценно обрабатывать сообщения, но может упасть уведомив спавнера о неудачном запуске.
// 2. Active --
//    Актор в нормальном состоянии и обрабатывает сообщения. Спавнер знает о готовности актора
// 3. Stopping --
//    Актор перестает принимать сообщения, но существующие в очереди он может обработать
// 4. Terminated --
//    Никакие сообщения актор не может обрабатывать.


[<Struct>]
[<RequireQualifiedAccess>]
type ActorStatus =
    | Inactive
    | Starting
    | Active
    | Stopping
    | Terminated

type UnhandleableMessage =
    inherit Exception
    new () = { inherit Exception() }
    new (message: string) = { inherit Exception(message) }

type UnsupportedMessageReply =
    inherit UnhandleableMessage
    new () = { inherit UnhandleableMessage() }
    new (message: string) = { inherit UnhandleableMessage(message) }


type IActorContext =

    abstract SelfAddress: IDynamicAddress

    abstract Status: ActorStatus

    /// <summary>
    /// Stop receiving new messages and switch to Stopping status.
    /// All already queued messages will be ignored if actor not restored from stopping.
    /// </summary>
    abstract Stop: unit -> unit


[<Interface>]
type IActor =

    abstract Receive: ctx: IActorContext * envelope: IEnvelope -> Future<unit>

    abstract Start: ctx: IActorContext -> Future<unit>

    abstract Stop: ctx: IActorContext -> Future<unit>


[<AbstractClass>]
type BaseActor() =
    abstract Receive: ctx: IActorContext * dynMsg: IEnvelope -> Future<unit>
    interface IActor with
        member this.Receive(ctx, dynMsg) =
            this.Receive(ctx, dynMsg)
        member this.Start(_ctx) = Future.unit'
        member this.Stop(_ctx) = Future.unit'
