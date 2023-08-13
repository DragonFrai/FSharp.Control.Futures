[<AutoOpen>]
module FSharp.Control.Futures.Extensions

open System
open System.Threading

open FSharp.Control.Futures
open FSharp.Control.Futures.Types
open FSharp.Control.Futures.Internals


type FutureFuseException(message: string) = inherit Exception(message)
type FutureFuseReadyException() = inherit FutureFuseException("Future was polled after returning Ready.")
type FutureFuseCancelledException() = inherit FutureFuseException("Future was polled after being cancelled.")
type FutureFuseTransitedException() = inherit FutureFuseException("Future was polled after returning Transit.")


[<RequireQualifiedAccess>]
module Future =

    [<Literal>] let SleepInit = 0
    [<Literal>] let SleepWaiting = 1
    [<Literal>] let SleepTimeout = 2
    [<Literal>] let SleepCancelled = 2

    type Sleep(duration: TimeSpan) =
        let mutable _sync = SpinLock(false)
        let mutable _timer: Timer = nullObj
        let mutable _context: IContext = nullObj
        let mutable _state: int = SleepInit

        member internal this.OnWake() : unit =
            let mutable hasLock = false
            _sync.Enter(&hasLock)
            match _state with
            | SleepWaiting ->
                let context, timer = _context, _timer
                _context <- nullObj
                _timer <- nullObj
                _state <- SleepTimeout
                if hasLock then _sync.Exit()
                context.Wake()
                timer.Dispose()
                ()
            | SleepCancelled ->
                if hasLock then _sync.Exit()
                raise FutureTerminatedException
            | SleepInit
            | SleepTimeout
            | _ ->
                if hasLock then _sync.Exit()
                unreachable ()

        interface Future<unit> with
            member this.Poll(ctx) =
                let mutable hasLock = false
                _sync.Enter(&hasLock)
                match _state with
                | SleepWaiting ->
                    if hasLock then _sync.Exit()
                    Poll.Pending
                | SleepInit ->
                    _context <- ctx
                    _timer <- new Timer((fun _ -> this.OnWake()), null, duration, Timeout.InfiniteTimeSpan)
                    _state <- SleepWaiting
                    if hasLock then _sync.Exit()
                    Poll.Pending
                | SleepTimeout ->
                    if hasLock then _sync.Exit()
                    Poll.Ready ()
                | SleepCancelled ->
                    if hasLock then _sync.Exit()
                    raise FutureTerminatedException
                | _ ->
                    if hasLock then _sync.Exit()
                    unreachable ()

            member _.Cancel() =
                let mutable hasLock = false
                _sync.Enter(&hasLock)
                match _state with
                | SleepInit ->
                    _state <- SleepCancelled
                    if hasLock then _sync.Exit()
                | SleepTimeout | SleepCancelled ->
                    if hasLock then _sync.Exit()
                    raise FutureTerminatedException
                | SleepWaiting ->
                    let context, timer = _context, _timer
                    _context <- nullObj
                    _timer <- nullObj
                    _state <- SleepTimeout
                    timer.Dispose()
                    if hasLock then _sync.Exit()
                | _ ->
                    if hasLock then _sync.Exit()
                    unreachable ()


    type Fuse<'a>(fut: Future<'a>) =
        let mutable isReady = false
        let mutable isCancelled = false
        let mutable isTransited = false
        interface Future<'a> with
            member _.Poll(ctx) =
                if isReady then raise (FutureFuseReadyException())
                elif isCancelled then raise (FutureFuseCancelledException())
                elif isTransited then raise (FutureFuseTransitedException())
                else
                    let p = Future.poll ctx fut
                    match p with
                    | Poll.Pending -> Poll.Pending
                    | Poll.Ready x ->
                        isReady <- true
                        Poll.Ready x
                    | Poll.Transit f ->
                        isTransited <- true
                        Poll.Transit f
            member _.Cancel() =
                isCancelled <- true


    //#region OS
    let sleep (duration: TimeSpan) : Future<unit> =
        upcast Sleep(duration)

    let sleepMs (millisecondDuration: int) =
        let duration = TimeSpan(days=0, hours=0, minutes=0, seconds=0, milliseconds=millisecondDuration)
        sleep duration

    /// <summary>
    /// Creates a Future that will throw a specific <see cref="FutureFuseException">FutureFuseException</see> if polled after returning Ready or Transit or being cancelled.
    /// </summary>
    /// <exception cref="FutureFuseReadyException">Throws if Future was polled after returning Ready.</exception>
    /// <exception cref="FutureFuseTransitedException">Throws if Future was polled after returning Transit.</exception>
    /// <exception cref="FutureFuseCancelledException">Throws if Future was polled after being cancelled.</exception>
    let fuse (fut: Future<'a>) : Future<'a> =
        upcast Fuse<'a>(fut)

