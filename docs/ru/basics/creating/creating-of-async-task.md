# Создание Future из Async и Task

Существующие Async и Task можно преобразовать в Future и использовать результат их работы.
Исходные Async и Task будут запущены на своих родных системах запуска, но их результат будет
передан через возвращенную Future.

```fsharp
let asyncToFuture = Future.ofAsync (async { ... })
let taskToFuture = Future.ofTask (task { ... })
```

Возможны и обратные преобразования. При этом Future будут запущены на механизме запуска
соответствующего примитива при запуске этого примитива.

```fsharp
let futureToAsync = Future.ofAsync (async { ... })
let futureToTask = Future.ofTask (task { ... })
```

