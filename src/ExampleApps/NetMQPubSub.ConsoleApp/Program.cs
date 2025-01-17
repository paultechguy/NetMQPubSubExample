﻿namespace NetMQPubSub.ConsoleApp;

using NetMQPubSub.Publisher;
using NetMQPubSub.Common.Helpers;
using NetMQPubSub.Subscriber;
using System;
using System.Linq;
using System.Text.Json;
using NetMQPubSub.Core.Interfaces;

internal class Program
{
	private const int MaximumSubscribers = 10;
	private readonly List<string> topics = Enumerable
		.Range(0, MaximumSubscribers)
		.Select(t => $"Topic{t}")
		.ToList();

	static void Main()
	{
		new Program().Run();
		NetMQPubSubHelper.Cleanup(block: false);
	}

	private void Run()
	{
		Console.Write("Press Enter to begin.  Once running, press Enter again to stop.");
		Console.ReadLine();

		var cancellationTokenSource = new CancellationTokenSource();
		var server = Task.Run(() => SubscribersAsync(cancellationTokenSource.Token));
		var client = Task.Run(() => PublisherAsync(cancellationTokenSource.Token));

		Console.ReadLine();

		cancellationTokenSource.Cancel();
		Task.WaitAll(server, client);
	}

	private void SubscribersAsync(CancellationToken cancelToken)
	{
		// or use "inproc://{name}" for in-process (e.g. inproc://job-service)
		var addr = "tcp://localhost:12345";

		var subscriberTasks = new List<Task>();
		for (var i = 0; i < topics.Count; i++)
		{
			var topicIndex = i;
			subscriberTasks.Add(Task.Run(() => RunTopicSubscriberAsync(topics[topicIndex], addr, topicIndex, cancelToken), cancelToken));
		}

		Task.WaitAll(subscriberTasks.ToArray());
	}

	private void PublisherAsync(CancellationToken cancelToken)
	{
		// or use "inproc://{name}" for in-process (e.g. inproc://job-service)
		var addr = "tcp://localhost:12345";

		using IMessagePublisher publisher = new MessagePublisher();
		publisher.Options.SendHighWatermark = 1000;

		Console.WriteLine("Publisher socket binding...");
		publisher.Bind(addr);

		// now that we've bound a socket, give subscriber a bit of time to initialize
		// before we begin sending messages
		Thread.Sleep(1000);

		var counter = 0;
		var rand = new Random();
		do
		{
			var randomizedTopic = rand.Next(this.topics.Count);
			var topic = this.topics[randomizedTopic];
			++counter;

			Console.WriteLine($"==> Sending message for topic \"{topic}\". Message: #{counter}");
			publisher.SendTopicMessage(topic, new TestMessage() { Counter = counter });

			// simulate a bit of processing
			Thread.Sleep(50);

		} while (!cancelToken.IsCancellationRequested);

		publisher.Close(); // also consider Unbind(addr)
		Console.WriteLine($"==> Publisher done!");
	}

	private static void RunTopicSubscriberAsync(string topic, string addr, int id, CancellationToken cancelToken)
	{
		Console.WriteLine($"Subscriber #{id} socket connecting...");

		using IMessageSubscriber subscriber = new MessageSubscriber();
		subscriber.Options.ReceiveHighWatermark = 1000;
		subscriber.Connect(addr);
		subscriber.TopicSubscribe(topic);

		const int maxSecondsDelayBeforeCancelCheck = 2;
		var timeout = TimeSpan.FromSeconds(maxSecondsDelayBeforeCancelCheck);
		do
		{
			if (subscriber.TryReceiveMessage<TestMessage>(timeout, out var messageTopicReceived, out var entityReceived))
			{
				Console.WriteLine($"<== Subscriber #{id} message for topic \"{messageTopicReceived}\". Message: {JsonSerializer.Serialize(entityReceived)}");
			}

		} while (!cancelToken.IsCancellationRequested);

		subscriber.Close(); // also consider Disconnect(addr)
		Console.WriteLine($"<== Subscriber #{id} done!");
	}

	internal class TestMessage
	{
		public int Counter { get; set; }

		public string Name { get; set; }

		public DateTime Now { get; set; }

		public TestMessage()
		{
			var names = new string[] { "Joe", "Sally", "Mary", "Steve", "Iris", "Bob" };
			var random = new Random();
			this.Counter = 0;
			this.Name = names[random.Next(names.Length)];
			this.Now = DateTime.Now;
		}
	}
}
