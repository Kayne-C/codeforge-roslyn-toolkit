using Xunit;

namespace CodeForge.Tests;

public class AsyncAnalyzerTests
{
    private const string Prelude = "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\n";

    [Fact]
    public Task Result_on_a_task_is_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            int M(Task<int> task) => {|CF0002:task.Result|};
        }
        """);

    [Fact]
    public Task Result_on_a_value_task_is_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            int M(ValueTask<int> task) => {|CF0002:task.Result|};
        }
        """);

    [Fact]
    public Task Result_on_an_unrelated_type_is_ignored() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class Operation { public int Result => 1; }
        class C
        {
            int M(Operation operation) => operation.Result;
        }
        """);

    [Fact]
    public Task Wait_and_static_waits_are_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            void M(Task a, Task b)
            {
                {|CF0002:a.Wait()|};
                {|CF0002:Task.WaitAll(a, b)|};
                {|CF0002:Task.WaitAny(a, b)|};
                {|CF0002:a.Wait(100)|};
            }
        }
        """);

    [Fact]
    public Task Blocking_through_the_awaiter_is_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            void M(Task a, Task<int> b)
            {
                {|CF0002:a.GetAwaiter().GetResult()|};
                var x = {|CF0002:b.ConfigureAwait(false).GetAwaiter().GetResult()|};
            }
        }
        """);

    [Fact]
    public Task Awaiting_is_never_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            async Task<int> M(Task<int> task, Task other)
            {
                await other;
                return await task;
            }
        }
        """);

    [Fact]
    public Task Thread_sleep_in_an_async_method_is_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            async Task M()
            {
                {|CF0006:Thread.Sleep(10)|};
                await Task.Delay(1);
            }
        }
        """);

    [Fact]
    public Task Thread_sleep_in_an_async_lambda_is_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            Func<Task> F = async () =>
            {
                {|CF0006:Thread.Sleep(TimeSpan.FromMilliseconds(1))|};
                await Task.Yield();
            };
        }
        """);

    [Fact]
    public Task Thread_sleep_in_a_synchronous_method_is_allowed() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            void M() => Thread.Sleep(10);
        }
        """);

    [Fact]
    public Task Async_void_is_reported() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            async void {|CF0005:Run|}()
            {
                await Task.Delay(1);
            }
        }
        """);

    [Fact]
    public Task Event_handlers_may_be_async_void() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            async void OnClick(object sender, EventArgs e)
            {
                await Task.Delay(1);
            }

            async void OnCustom(object sender, MyArgs e)
            {
                await Task.Delay(1);
            }
        }
        class MyArgs : EventArgs { }
        """);

    [Fact]
    public Task Async_task_methods_are_fine() => AnalyzerVerifier.VerifyAsync<AsyncAnalyzer>(Prelude + """
        class C
        {
            async Task Run() => await Task.Delay(1);
        }
        """);
}

public class AsyncCodeFixTests
{
    private const string Prelude = "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\n";

    [Fact]
    public Task Result_becomes_an_await() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, SyncOverAsyncCodeFix>(
        Prelude + """
        class C
        {
            async Task<int> M(Task<int> task)
            {
                var x = {|CF0002:task.Result|};
                return x;
            }
        }
        """,
        Prelude + """
        class C
        {
            async Task<int> M(Task<int> task)
            {
                var x = await task;
                return x;
            }
        }
        """);

    [Fact]
    public Task Result_inside_an_expression_is_parenthesised() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, SyncOverAsyncCodeFix>(
        Prelude + """
        class C
        {
            async Task<string> M(Task<int> task) => {|CF0002:task.Result|}.ToString();
        }
        """,
        Prelude + """
        class C
        {
            async Task<string> M(Task<int> task) => (await task).ToString();
        }
        """);

    [Fact]
    public Task Wait_becomes_an_await_statement() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, SyncOverAsyncCodeFix>(
        Prelude + """
        class C
        {
            async Task M(Task task)
            {
                {|CF0002:task.Wait()|};
            }
        }
        """,
        Prelude + """
        class C
        {
            async Task M(Task task)
            {
                await task;
            }
        }
        """);

    [Fact]
    public Task Get_result_through_the_awaiter_becomes_an_await() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, SyncOverAsyncCodeFix>(
        Prelude + """
        class C
        {
            async Task<int> M(Task<int> task)
            {
                return {|CF0002:task.GetAwaiter().GetResult()|};
            }
        }
        """,
        Prelude + """
        class C
        {
            async Task<int> M(Task<int> task)
            {
                return await task;
            }
        }
        """);

    [Fact]
    public Task No_fix_is_offered_when_the_method_is_not_async() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, SyncOverAsyncCodeFix>(
        Prelude + """
        class C
        {
            int M(Task<int> task) => {|CF0002:task.Result|};
        }
        """,
        Prelude + """
        class C
        {
            int M(Task<int> task) => {|CF0002:task.Result|};
        }
        """);

    [Fact]
    public Task Async_void_becomes_task() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, AsyncVoidCodeFix>(
        Prelude + """
        class C
        {
            async void {|CF0005:Run|}()
            {
                await Task.Delay(1);
            }
        }
        """,
        Prelude + """
        class C
        {
            async Task Run()
            {
                await Task.Delay(1);
            }
        }
        """);

    [Fact]
    public Task Thread_sleep_becomes_task_delay() => AnalyzerVerifier.VerifyFixAsync<AsyncAnalyzer, BlockingSleepCodeFix>(
        Prelude + """
        class C
        {
            async Task M()
            {
                {|CF0006:Thread.Sleep(250)|};
            }
        }
        """,
        Prelude + """
        class C
        {
            async Task M()
            {
                await Task.Delay(250);
            }
        }
        """);
}
