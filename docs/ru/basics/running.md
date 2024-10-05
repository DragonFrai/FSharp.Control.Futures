# Запуск Future


## Запуск на текущем потоке

Future можно запустить на текущем потоке используя `Future.runBlocking`.
Переданная Future запустится, а вызывающий поток будет заблокирован
пока не получится результат.
```fsharp
let fut = future {
    let! name = Future.ready "Alex"
    do! Future.sleepMs 1000
    return $"Hello, {name}!"
}

let phrase = fut |> Future.runBlocking
printfn $"{phrase}"
```


## Запуск используя Runtime

Future можно запустить на Runtime.
Runtime это планировщик для нескольких параллельно выполняющихся Future,
не используя Future.merge и снимая его ограничения
(Future скомбинированные используя Future.merge никогда не выполняются по-настоящему параллельно).

Запустить Future на планировщике можно используя его метод Spawn.

```fsharp
let fut = future { ... }
let fTask = ThreadPoolRuntime.Instance.Spawn(fut)
```

Spawn возвращает объект запущенной задачи (IFutureTask<'a>).
Используя экземпляр запущенной задачи можно преобразовать её в ожидающую выполнения
Future используя Await, или прервать её выполнение через Abort.
Если задача была прервана, ожидающая Future выбросит исключение при своем запуске.

```fsharp
future {
    let fTask = ThreadPoolRuntime.Instance.Spawn(future { ... })
    do! doOtherWork ()
    let! fTaskResult = fTask.Await()
}
```

<div class="warning">
IFutureTask.Await может быть вызван только один раз.
</div>

По-умолчанию Await создает Future, вызывающую Abort при своем Drop.
Это можно переопределить вызвав Await с флагом background=true (`fTask.Await(true)`).
