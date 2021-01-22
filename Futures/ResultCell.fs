namespace Futures

open System
open System.Threading

[<AutoOpen>]
module private Halpers =
    let fake ()  = Unchecked.defaultof<AsyncReturn>
    let unfake (_ : AsyncReturn)  = ()

type ResultCell<'a>() =

    let mutable result = None

    // The WaitHandle event for the result. Only created if needed, and set to null when disposed.
    let mutable resEvent = null

    let mutable disposed = false
    
    let mutable onRegisterResult = fun () -> ()

    // All writers of result are protected by lock on syncRoot.
    let syncRoot = new Object()

    member x.GetWaitHandle() =
        lock syncRoot (fun () ->
            if disposed then
                raise (System.ObjectDisposedException("ResultCell"))
            match resEvent with
            | null ->
                // Start in signalled state if a result is already present.
                let ev = new ManualResetEvent(result.IsSome)
                resEvent <- ev
                ev
            | ev ->
                ev)
    
    member this.SetOnRegisterResultNoLock(f) =
        onRegisterResult <- f
    
    member x.Close() =
        lock syncRoot (fun () ->
            if not disposed then
                disposed <- true
                match resEvent with
                | null -> ()
                | ev ->
                    ev.Close()
                    resEvent <- null)

    interface IDisposable with
        member x.Dispose() = x.Close()

    member x.GrabResult() =
        match result with
        | Some res -> res
        | None -> failwith "Unexpected no result"
    
    member private x.RegisterResultNoLock (res:'a) =
        // Ignore multiple sets of the result. This can happen, e.g. for a race between a cancellation and a success
        if x.ResultAvailable then
            invalidOp "Double RegisterResult"
        else
            if disposed then
                ()
            else
                result <- Some res
                onRegisterResult ()
                // If the resEvent exists then set it. If not we can skip setting it altogether and it won't be
                // created
                match resEvent with
                | null ->
                    ()
                | ev ->
                    // Setting the event need to happen under lock so as not to race with Close()
                    ev.Set () |> ignore
    
    
    /// Record the result in the ResultCell.
    member x.RegisterResult (res: 'a) =
        lock syncRoot (fun () ->
            x.RegisterResultNoLock(res)
        )
    
    member x.RegisterResultIfNotAvailable (res: 'a) =
        lock syncRoot (fun () ->
            // Ignore multiple sets of the result. This can happen, e.g. for a race between a cancellation and a success
            if not x.ResultAvailable then
                x.RegisterResultNoLock(res)
        )

    member x.ResultAvailable = result.IsSome

    member x.TryWaitForResultSynchronously (?timeout) : 'a option =
        // Check if a result is available.
        match result with
        | Some _ as r ->
            r
        | None ->
            // Force the creation of the WaitHandle
            let resHandle = x.GetWaitHandle()
            // Check again. While we were in GetWaitHandle, a call to RegisterResult may have set result then skipped the
            // Set because the resHandle wasn't forced.
            match result with
            | Some _ as r ->
                r
            | None ->
                // OK, let's really wait for the Set signal. This may block.
                let timeout = defaultArg timeout Threading.Timeout.Infinite
                let ok = resHandle.WaitOne(millisecondsTimeout= timeout, exitContext=true)
                if ok then
                    // Now the result really must be available
                    result
                else
                    // timed out
                    None