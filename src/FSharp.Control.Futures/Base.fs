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


    // TODO: optimize
    let parallelSeq (futures: IFuture<'a> seq) : IFuture<'a[]> =
        let mutable futures = futures |> Seq.map ValueSome |> Seq.toArray
        let mutable results: 'a[] = Array.zeroCreate (Array.length futures)

        Future.create ^fun waker ->
            futures
            |> Seq.indexed
            |> Seq.map (fun (i, f) ->
                match f with
                | ValueSome f ->
                    let p = Future.poll waker f
                    match p with
                    | Ready value ->
                        futures.[i] <- ValueNone
                        results.[i] <- value
                        true
                    | Pending -> false
                | ValueNone -> true
            )
            |> Seq.reduce (&&)
            |> fun x ->
                match x with
                | false -> Pending
                | true -> Ready results

    let catch (f: IFuture<'a>) : IFuture<Result<'a, exn>> =
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
    let run (f: IFuture<'a>) : 'a =
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let waker () = wh.Set |> ignore

        let rec wait (current: Poll<'a>) =
            match current with
            | Ready x -> x
            | Pending ->
                wh.WaitOne() |> ignore
                wait (Future.poll waker f)

        wait (Future.poll waker f)




