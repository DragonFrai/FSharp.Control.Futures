module FSharp.Control.Futures.Sync

open System

// Sync primitives orientated on future

/// OneShor sync channel
// TODO: Optimize if possible
module OneShot =

    [<Struct>]
    type internal OneShotError =
        // Used for throwing errors including on the receiver side
        SenderDisposedBeforeSend

    //
    [<Class>]
    type internal Inner<'a>() =

        let mutable value: Result<'a, OneShotError> voption = ValueNone
        // TODO: Make nullable ?
        let mutable receiverWaker: Waker voption = ValueNone

        member inline it.PutAndWake(v) =
            lock it ^fun () ->
                value <- ValueSome v
                match receiverWaker with
                | ValueSome waker -> waker()
                | ValueNone -> ()

        member inline private it.TryGetValueNoLock() =
            lock it ^fun () ->
                match value with
                | ValueSome res ->
                    match res with
                    | Ok x -> ValueSome x
                    | Error err ->
                        match err with
                        | SenderDisposedBeforeSend -> invalidOp "OneShotSender disposed before send"
                | ValueNone -> ValueNone

        member inline it.TryGetValue() =
            lock it ^it.TryGetValueNoLock

        member inline it.GetOrWait(rWaker: Waker) =
            lock it ^fun () ->
                match it.TryGetValueNoLock() with
                | ValueSome x -> ValueSome x
                | ValueNone ->
                    receiverWaker <- ValueSome rWaker
                    ValueNone

    /// Sender of oneshot message.
    /// Send only one value. On send second value throw exn.
    /// Must not use more from one thread.
    [<Struct>]
    type OneShotSender<'a> =

        // None if already send
        val mutable private inner: Inner<'a> voption
        internal new(inner: Inner<'a>) = { inner = ValueSome inner }

        member it.Send(value: 'a) =
            match it.inner with
            | ValueNone -> invalidOp "Double send or send after dispose"
            | ValueSome inner ->
                inner.PutAndWake(Ok value)
                it.inner <- ValueNone

        // Dispose now used only for signalize about invalid use and keep out the ever-waiting receivers
        interface IDisposable with
            member it.Dispose() =
                match it.inner with
                | ValueNone -> () // All ok
                | ValueSome inner -> // Signal the receiver about impossible receive
                    inner.PutAndWake (Error OneShotError.SenderDisposedBeforeSend)
                    it.inner <- ValueNone
                    invalidOp "Dispose OneShotSender before send"
                    ()


    /// Receiver of oneshot message. Must not use more from one thread
    [<Struct>]
    type OneShotReceiver<'a> =
        // TODO: Remove already receive check ?
        /// None if already received or transform into wait future
        val mutable private inner: Inner<'a> voption
        internal new(inner: Inner<'a>) = { inner = ValueSome inner }

        member inline private it.GetInner() : Inner<'a> =
            match it.inner with
            | ValueNone -> invalidOp "Already received or convert into wait future"
            | ValueSome inner -> inner

        member inline it.TryReceive() : ValueOption<'a> =
            let inner = it.GetInner()
            let value = inner.TryGetValue()
            if value.IsSome then it.inner <- ValueNone
            value

        member inline it.Receive() : Future<'a> =
            let inner = it.GetInner()
            it.inner <- ValueNone

            let mutable value = ValueNone
            FutureCore.create ^fun waker ->
                match value with
                | ValueSome x -> Ready x
                | ValueNone ->
                    let value' = inner.GetOrWait(waker)
                    value <- value'
                    match value' with
                    | ValueSome x -> Ready x
                    | ValueNone -> Pending

    ()
