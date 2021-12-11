namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures

// TODO: Add thread safety sink version. IConnectableSink


exception SinkClosedException


/// Сток. Позволяет отправлять данные в связанный поток или куда-нибудь еще.
/// Когда сток закрывается, поток переходит в состояние Completed.
/// Так как поток получает данные из стока в произвольные моменты времени,
/// не имеет смылсла обрабатывать закрытие потокак как ошибку отправки.
/// Однако информирование о закрытии потокак когда это уже известно, может быть полезным.
///
/// Sink не является потокобезопасным сам по себе.
type ISink<'a> =
    /// Отправляет значение в Sink. Возвращает false, если связанный Stream уже закрыт
    /// и true, если сообщение может быть получено.
    /// Если состояние Stream неопределено, отправка всегда считается успешной
    /// и не может служить маркером прекращения приема во всех случаях.
    abstract Add: element: 'a -> bool
    /// Закрывает Sink. После закрытия отправка значений должна быть прекращена.
    abstract Close: unit -> unit

module Sink =
    let inline add (element: 'a) (sink: ISink<'a>): unit =
        sink.Add(element) |> ignore

    // TODO: rename
    let inline addChecked (element: 'a) (sink: ISink<'a>): bool =
        sink.Add(element)

    let inline close (sink: ISink<'a>): unit =
        sink.Close()

    let ignored (): ISink<'a> =
        let mutable isClosed = false
        { new ISink<'a> with
            member _.Add(_x) =
                if isClosed then raise SinkClosedException
                true

            member _.Close() =
                isClosed <- true
        }


type IAsyncSink<'a> =
    abstract Add: element: 'a -> Future<bool>
    abstract Close: unit -> unit

module AsyncSink =
    let inline add (element: 'a) (sink: IAsyncSink<'a>): Future<unit> =
        sink.Add(element) |> Future.ignore

    // TODO: rename
    let inline addChecked (element: 'a) (sink: IAsyncSink<'a>): Future<bool> =
        sink.Add(element)

    let inline close (sink: IAsyncSink<'a>): unit =
        sink.Close()

    let ofSink (sink: ISink<'a>): IAsyncSink<'a> =
        { new IAsyncSink<'a> with
             member _.Add(x) = Future.lazy' (fun () -> sink.Add(x))
             member _.Close() = sink.Close() }
