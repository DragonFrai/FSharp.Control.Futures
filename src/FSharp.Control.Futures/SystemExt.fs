[<AutoOpen>]
module FSharp.Control.Futures.SystemExt

open System
open System.Collections.Generic
open System.Threading
open System.Timers


// Includes an extension to the base methods of Future,
// which provides interoperability functionality that potentially
// requires interaction with the OS, such as timers, threads, etc.

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
                lock sync ^fun () ->
                    timer <- None
                    t.Dispose()
                    match currentWaker with
                    | Some w -> w ()
                    | None -> ()
            )
            Some t

        Future.Core.create ^fun waker ->
            lock sync ^fun () ->
                match timer with
                | Some timer ->
                    currentWaker <- Some waker
                    if not timer.Enabled then timer.Start()
                    Poll.Pending
                | None ->
                    Poll.Ready ()

    let runSync (f: Future<'a>) : 'a =
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let waker () = wh.Set() |> ignore

        let rec wait (current: Poll<'a>) =
            match current with
            | Poll.Ready x -> x
            | Poll.Pending ->
                wh.WaitOne() |> ignore
                wait (Future.Core.poll waker f)

        wait (Future.Core.poll waker f)

