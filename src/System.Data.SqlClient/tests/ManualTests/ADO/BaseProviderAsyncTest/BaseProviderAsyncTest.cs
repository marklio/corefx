// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace System.Data.SqlClient.ManualTesting.Tests
{
    public class BaseProviderAsyncTest
    {
        [Fact]
        public void TestDbConnection()
        {
            MockConnection connection = new MockConnection();
            CancellationTokenSource source = new CancellationTokenSource();

            // ensure OpenAsync() calls OpenAsync(CancellationToken.None)
            DataTestClass.AssertEqualsWithDescription(ConnectionState.Closed, connection.State, "Connection state should have been marked as Closed");
            connection.OpenAsync().Wait();
            Assert.False(connection.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            DataTestClass.AssertEqualsWithDescription(ConnectionState.Open, connection.State, "Connection state should have been marked as Open");
            connection.Close();

            // Verify cancellationToken over-ride
            DataTestClass.AssertEqualsWithDescription(ConnectionState.Closed, connection.State, "Connection state should have been marked as Closed");
            connection.OpenAsync(source.Token).Wait();
            DataTestClass.AssertEqualsWithDescription(ConnectionState.Open, connection.State, "Connection state should have been marked as Open");
            connection.Close();

            // Verify exceptions are routed through task
            MockConnection connectionFail = new MockConnection()
            {
                Fail = true
            };
            connectionFail.OpenAsync().ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            connectionFail.OpenAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();

            // Verify base implementation does not call Open when passed an already cancelled cancelation token
            source.Cancel();
            DataTestClass.AssertEqualsWithDescription(ConnectionState.Closed, connection.State, "Connection state should have been marked as Closed");
            connection.OpenAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription(ConnectionState.Closed, connection.State, "Connection state should have been marked as Closed");
        }

        [Fact]
        public void TestDbCommand()
        {
            MockCommand command = new MockCommand()
            {
                ScalarResult = 1,
                Results = Enumerable.Range(1, 5).Select((x) => new object[] { x, x.ToString() })
            };
            CancellationTokenSource source = new CancellationTokenSource();

            // Verify parameter routing and correct synchronous implementation is called
            command.ExecuteNonQueryAsync().Wait();
            Assert.False(command.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            DataTestClass.AssertEqualsWithDescription("ExecuteNonQuery", command.LastCommand, "Last command was not as expected");
            command.ExecuteReaderAsync().Wait();
            DataTestClass.AssertEqualsWithDescription(CommandBehavior.Default, command.CommandBehavior, "Command behavior should have been marked as Default");
            Assert.False(command.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            DataTestClass.AssertEqualsWithDescription("ExecuteReader", command.LastCommand, "Last command was not as expected");
            command.ExecuteScalarAsync().Wait();
            Assert.False(command.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            DataTestClass.AssertEqualsWithDescription("ExecuteScalar", command.LastCommand, "Last command was not as expected");

            command.ExecuteNonQueryAsync(source.Token).Wait();
            DataTestClass.AssertEqualsWithDescription("ExecuteNonQuery", command.LastCommand, "Last command was not as expected");
            command.ExecuteReaderAsync(source.Token).Wait();
            DataTestClass.AssertEqualsWithDescription("ExecuteReader", command.LastCommand, "Last command was not as expected");
            command.ExecuteScalarAsync(source.Token).Wait();
            DataTestClass.AssertEqualsWithDescription("ExecuteScalar", command.LastCommand, "Last command was not as expected");

            command.ExecuteReaderAsync(CommandBehavior.SequentialAccess).Wait();
            DataTestClass.AssertEqualsWithDescription(CommandBehavior.SequentialAccess, command.CommandBehavior, "Command behavior should have been marked as SequentialAccess");
            Assert.False(command.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            DataTestClass.AssertEqualsWithDescription("ExecuteReader", command.LastCommand, "Last command was not as expected");

            command.ExecuteReaderAsync(CommandBehavior.SingleRow, source.Token).Wait();
            DataTestClass.AssertEqualsWithDescription(CommandBehavior.SingleRow, command.CommandBehavior, "Command behavior should have been marked as SingleRow");
            DataTestClass.AssertEqualsWithDescription("ExecuteReader", command.LastCommand, "Last command was not as expected");

            // Verify exceptions are routed through task
            MockCommand commandFail = new MockCommand
            {
                Fail = true
            };
            commandFail.ExecuteNonQueryAsync().ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteNonQueryAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteReaderAsync().ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteReaderAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteReaderAsync(CommandBehavior.SequentialAccess, source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteScalarAsync().ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            commandFail.ExecuteScalarAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();

            // Verify base implementation does not call Open when passed an already cancelled cancelation token
            source.Cancel();
            command.LastCommand = "Nothing";
            command.ExecuteNonQueryAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", command.LastCommand, "Expected last command to be 'Nothing'");
            command.ExecuteReaderAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", command.LastCommand, "Expected last command to be 'Nothing'");
            command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", command.LastCommand, "Expected last command to be 'Nothing'");
            command.ExecuteScalarAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", command.LastCommand, "Expected last command to be 'Nothing'");

            // Verify cancelation
            command.WaitForCancel = true;
            source = new CancellationTokenSource();
            Task.Factory.StartNew(() => { command.WaitForWaitingForCancel(); source.Cancel(); });
            Task result = command.ExecuteNonQueryAsync(source.Token);
            Assert.True(result.IsFaulted, "Task result should be faulted");

            source = new CancellationTokenSource();
            Task.Factory.StartNew(() => { command.WaitForWaitingForCancel(); source.Cancel(); });
            result = command.ExecuteReaderAsync(source.Token);
            Assert.True(result.IsFaulted, "Task result should be faulted");

            source = new CancellationTokenSource();
            Task.Factory.StartNew(() => { command.WaitForWaitingForCancel(); source.Cancel(); });
            result = command.ExecuteScalarAsync(source.Token);
            Assert.True(result.IsFaulted, "Task result should be faulted");
        }

        [Fact]
        public void TestDbDataReader()
        {
            var query = Enumerable.Range(1, 2).Select((x) => new object[] { x, x.ToString(), DBNull.Value });
            MockDataReader reader = new MockDataReader { Results = query.GetEnumerator() };
            CancellationTokenSource source = new CancellationTokenSource();

            Task<bool> result;

            result = reader.ReadAsync(); result.Wait();
            DataTestClass.AssertEqualsWithDescription("Read", reader.LastCommand, "Last command was not as expected");
            Assert.True(result.Result, "Should have received a Result from the ReadAsync");
            Assert.False(reader.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");

            GetFieldValueAsync(reader, 0, 1);
            Assert.False(reader.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            GetFieldValueAsync(reader, source.Token, 1, "1");

            result = reader.ReadAsync(source.Token); result.Wait();
            DataTestClass.AssertEqualsWithDescription("Read", reader.LastCommand, "Last command was not as expected");
            Assert.True(result.Result, "Should have received a Result from the ReadAsync");

            GetFieldValueAsync<object>(reader, 2, DBNull.Value);
            GetFieldValueAsync<DBNull>(reader, 2, DBNull.Value);
            reader.GetFieldValueAsync<int?>(2).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            reader.GetFieldValueAsync<string>(2).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            reader.GetFieldValueAsync<bool>(2).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            DataTestClass.AssertEqualsWithDescription("GetValue", reader.LastCommand, "Last command was not as expected");

            result = reader.ReadAsync(); result.Wait();
            DataTestClass.AssertEqualsWithDescription("Read", reader.LastCommand, "Last command was not as expected");
            Assert.False(result.Result, "Should NOT have received a Result from the ReadAsync");

            result = reader.NextResultAsync();
            DataTestClass.AssertEqualsWithDescription("NextResult", reader.LastCommand, "Last command was not as expected");
            Assert.False(result.Result, "Should NOT have received a Result from NextResultAsync");
            Assert.False(reader.CancellationToken.CanBeCanceled, "Default cancellation token should not be cancellable");
            result = reader.NextResultAsync(source.Token);
            DataTestClass.AssertEqualsWithDescription("NextResult", reader.LastCommand, "Last command was not as expected");
            Assert.False(result.Result, "Should NOT have received a Result from NextResultAsync");

            MockDataReader readerFail = new MockDataReader { Results = query.GetEnumerator(), Fail = true };
            readerFail.ReadAsync().ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            readerFail.ReadAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            readerFail.NextResultAsync().ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            readerFail.NextResultAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            readerFail.GetFieldValueAsync<object>(0).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            readerFail.GetFieldValueAsync<object>(0, source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnFaulted).Wait();

            source.Cancel();
            reader.LastCommand = "Nothing";
            reader.ReadAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", reader.LastCommand, "Expected last command to be 'Nothing'");
            reader.NextResultAsync(source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", reader.LastCommand, "Expected last command to be 'Nothing'");
            reader.GetFieldValueAsync<object>(0, source.Token).ContinueWith((t) => { }, TaskContinuationOptions.OnlyOnCanceled).Wait();
            DataTestClass.AssertEqualsWithDescription("Nothing", reader.LastCommand, "Expected last command to be 'Nothing'");
        }

        private void GetFieldValueAsync<T>(MockDataReader reader, int ordinal, T expected)
        {
            Task<T> result = reader.GetFieldValueAsync<T>(ordinal);
            result.Wait();
            DataTestClass.AssertEqualsWithDescription("GetValue", reader.LastCommand, "Last command was not as expected");
            DataTestClass.AssertEqualsWithDescription(expected, result.Result, "GetFieldValueAsync did not return expected value");
        }

        private void GetFieldValueAsync<T>(MockDataReader reader, CancellationToken cancellationToken, int ordinal, T expected)
        {
            Task<T> result = reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
            result.Wait();
            DataTestClass.AssertEqualsWithDescription("GetValue", reader.LastCommand, "Last command was not as expected");
            DataTestClass.AssertEqualsWithDescription(expected, result.Result, "GetFieldValueAsync did not return expected value");
        }
    }
}
