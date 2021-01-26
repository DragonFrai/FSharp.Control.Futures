module FSharp.Control.Futures.Base

open System
open System.Threading
open System.Timers


[<RequireQualifiedAccess>]
module Future =

    let sleep (duration: int) =
        // if None the time out
        let mutable currentWaker = None
        let mutable timer = None
        let sync = obj()

        timer <-
            let t = new Timer(float duration)
            t.AutoReset <- false
            t.Elapsed.Add(fun _ ->
                // TODO: think!! Called from other thread
                timer <- None
                t.Dispose()
                lock sync ^fun () ->
                    match currentWaker with
                    | Some w -> w ()
                    | None -> ()
            )
            Some t
        Future.create ^fun waker ->
            match timer with
            | Some timer ->
                lock sync ^fun () ->
                    currentWaker <- Some waker
                    if not timer.Enabled then timer.Start()
                Pending
            | None ->
                Ready ()

    let catch (f: Future<'a>) : Future<Result<'a, exn>> =
        let mutable result = ValueNone
        Future.create ^fun waker ->
            if ValueNone = result then
                try
                    Future.poll waker f |> Poll.onReady ^fun x -> result <- ValueSome (Ok x)
                with
                | e -> result <- ValueSome (Error e)
            match result with
            | ValueSome r -> Ready r
            | ValueNone -> Pending

    // TODO: fix it
    let run (f: Future<'a>) : 'a =
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let waker () = wh.Set |> ignore

        let rec wait (current: Poll<'a>) =
            match current with
            | Ready x -> x
            | Pending ->
                wh.WaitOne() |> ignore
                wait (Future.poll waker f)

        wait (Future.poll waker f)




