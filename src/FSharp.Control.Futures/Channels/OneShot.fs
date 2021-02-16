module FSharp.Control.Futures.Channels.OneShot

open System
open FSharp.Control.Futures


exception SendingInterruptedException of string
exception DoubleSendException of string
exception DoubleReceiveException of string


// (I) == Interrupted                                                      |
//                                                                         |
//                                                                         |
//           Value ----------------------------------------\               |
//         /               FutureWaiting --- WaitedValue ---\              |
//       /                /             \                    \             |
//  Init --- FutureExists -- (I)        (I)                   - Received   |
//      \                \                                   /             |
//      (I)                WaitedValue --------------------/               |
//                                                                         |

[<Struct>]
type private OneShotState<'a> =
    | Init // -> Value / FutureExists / Interrupted
    // Value is ready. Any receive is possible
    | Value of value: 'a // ->  Received
    // Exists Future for receive. Any others receives are impossible
    | FutureExists // -> FutureWaiting / WaitedValue / Interrupted
    // Exists waiting value. Any others receives are impossible
    | FutureWaiting of waker: Waker // -> WaitedValue / Interrupted
    // Ready value for waited future
    | WaitedValue of wValue: 'a // -> Received
    | Received
    // Only when Disposed
    | Interrupted


// ReceiveOnce : unit -> Future<'a>

// TODO: optimize
type OneShotChannel<'a> =

    val mutable private state: OneShotState<'a>
    val private syncLock: obj

    new() = { state = OneShotState.Init; syncLock = obj() }

    member inline private this.SendImmediateNoLock(x) =
        match this.state with
        | Init -> this.state <- Value x
        | FutureExists -> this.state <- WaitedValue x
        | FutureWaiting waker -> this.state <- WaitedValue x; waker ()
        | Interrupted -> raise (SendingInterruptedException "Send to interrupted one shot channel")
        | Received | WaitedValue _ | Value _ -> raise (DoubleSendException "Double send to one shot channel")

    member this.SendImmediate(x) =
        lock this.syncLock ^fun () ->
            this.SendImmediateNoLock(x)

    // Пытается получить значение, если оно существует или возвращает Pending, когда оно может быть получено позже и не имеет других ожидающих получателей.
    // В противном случае бросает исключение
    /// Throws if the value has already been received (or exists waited future) and cannot be received in the future
    member this.PollReceive() : Poll<'a> =
        lock this.syncLock ^fun () ->
            match this.state with
            | Init -> Pending
            | Value x -> this.state <- Received; Ready x
            | Interrupted -> raise (SendingInterruptedException "Receive from interrupted one shot channel")
            | Received -> raise (DoubleReceiveException "Double receive from one shot channel")
            | FutureExists | FutureWaiting _ | WaitedValue _ ->
                raise (DoubleReceiveException "Double receive from one shot channel with waited future")

    interface IChannel<'a> with

        member this.Send(value: 'a) : Future<unit> =
            Future.lazy' ^fun () -> this.SendImmediate(value)

        member this.Receive() =
            lock this.syncLock ^fun () ->
                match this.state with
                | Value x -> this.state <- Received; Future.ready (Some x)
                | Init ->
                    this.state <- FutureExists
                    let mutable value = None
                    FutureCore.create ^fun waker ->
                        match value with
                        | Some _ -> Ready value
                        | None ->
                            lock this.syncLock ^fun () ->
                                match this.state with
                                | FutureExists ->
                                    this.state <- FutureWaiting waker
                                    Pending
                                | WaitedValue value' ->
                                    this.state <- Received
                                    value <- Some value'
                                    Ready value
                                | Interrupted -> Ready None
                                | FutureWaiting _ -> invalidOp "Future waked before put value"
                                | _ -> invalidOp "Invalid state"
                | FutureExists | FutureWaiting _ | WaitedValue _ | Received | Interrupted ->
                    Future.ready None

        // Dispose now used only for signalize about invalid use and keep out the ever-waiting receivers
        member this.Dispose() =
            lock this.syncLock ^fun () ->
                match this.state with
                | Init | FutureExists -> this.state <- Interrupted
                | FutureWaiting waker -> this.state <- Interrupted; waker()
                | Received | Value _ | WaitedValue _ -> ()
                | Interrupted -> raise (ObjectDisposedException "Double dispose")

let create<'a> () = new OneShotChannel<'a>()

let createPair<'a> () =
    let channel = new OneShotChannel<'a>()
    (channel :> ISender<'a>, channel :> IReceiver<'a>)

