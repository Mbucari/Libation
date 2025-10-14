using System;
using System.Diagnostics;
using System.Threading;

#nullable enable
namespace DataLayer;

public interface INotifyDispose<T> : IDisposable
{
	public event EventHandler? ObjectDisposed;
}

public static class InstanceQueue<T> where T : class, INotifyDispose<T>
{
	private static Lock Locker { get; } = new();
	private static Ticket? LastInLine { get; set; }

	public static T WaitToCreateInstance(Func<T> creator)
	{
		Ticket ticket;
		lock (Locker)
		{
			ticket = LastInLine = new Ticket(creator, LastInLine);
		}

		return ticket.Fulfill();
	}

	private class Ticket(Func<T> creator, Ticket? inFront) : IDisposable
	{
		private Func<T> Creator { get; } = creator;
		private Ticket? InFront { get; } = inFront;
		private EventWaitHandle? WaitHandle { get; set; }

		/// <summary>
		/// Disposes of this ticket instance an every ticket holder queued in front of it.
		/// </summary>
		public void Dispose()
		{
			WaitHandle?.Dispose();
			InFront?.Dispose();
		}

		/// <summary>
		/// Waits until <see cref="Instance_ObjectDisposed"/> has been fired.
		/// <para/>
		/// Only called by a ticket holder immediately behind this instance.
		/// If no instance queues behind this, no wait handle gets created.
		/// </summary>
		private void Wait()
		{
			WaitHandle = new(false, EventResetMode.ManualReset);
			WaitHandle.WaitOne(Timeout.Infinite);
			Dispose();
		}

		/// <summary>
		/// Wait's for the <see cref="InFront"/> ticket's instance to be disposed,
		/// then creates and returns a new instance of <see cref="T"/> using the
		/// <see cref="Creator"/> factory.
		/// <para>This ticket is disposed upon return</para>
		/// </summary>
		public T Fulfill()
		{
#if DEBUG
			var sw = Stopwatch.StartNew();
#endif
			InFront?.Wait();
#if DEBUG
			sw.Stop();
			Debug.WriteLine($"Waited {sw.ElapsedMilliseconds}ms to create instance of {typeof(T).Name}");
#endif
			var instance = Creator();
			instance.ObjectDisposed += Instance_ObjectDisposed;
			return instance;
		}

		private void Instance_ObjectDisposed(object? sender, EventArgs e)
		{
			Debug.WriteLine($"{typeof(T).Name} Disposed");
			if (sender is T instance)
			{
				instance.ObjectDisposed -= Instance_ObjectDisposed;
			}

			WaitHandle?.Set();
			lock (Locker)
			{
				if (this == LastInLine)
				{
					//There are no ticket holders waiting after this one
					LastInLine = null;
				}
			}
		}
	}
}
