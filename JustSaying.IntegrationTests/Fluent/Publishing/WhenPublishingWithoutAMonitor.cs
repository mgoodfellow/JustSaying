using System;
using System.Threading;
using System.Threading.Tasks;
using JustSaying.Messaging;
using JustSaying.Messaging.MessageHandling;
using JustSaying.Models;
using JustSaying.TestingFramework;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit.Abstractions;

namespace JustSaying.IntegrationTests.Fluent.Publishing
{
    public class WhenPublishingWithoutAMonitor : IntegrationTestBase
    {
        public WhenPublishingWithoutAMonitor(ITestOutputHelper outputHelper)
            : base(outputHelper)
        {
        }

        [AwsFact]
        public async Task A_Message_Can_Still_Be_Published_To_A_Queue()
        {
            // Arrange
            var completionSource = new TaskCompletionSource<object>();
            var handler = CreateHandler<SimpleMessage>(completionSource);

            IServiceCollection services = GivenJustSaying()
                .ConfigureJustSaying((builder) => builder.WithLoopbackQueue<SimpleMessage>(UniqueName))
                .AddSingleton(handler);

            // Act and Assert
            await AssertMessagePublishedAndReceivedAsync(services, handler, completionSource);
        }

        [AwsFact]
        public async Task A_Message_Can_Still_Be_Published_To_A_Topic()
        {
            // Arrange
            var completionSource = new TaskCompletionSource<object>();
            var handler = CreateHandler<SimpleMessage>(completionSource);

            IServiceCollection services = Given(
                (builder) =>
                {
                    builder.Publications((publication) => publication.WithTopic<SimpleMessage>());

                    builder.Messaging(
                        (config) => config.WithPublishFailureBackoff(TimeSpan.FromMilliseconds(1))
                                          .WithPublishFailureReattempts(1));

                    builder.Subscriptions(
                        (subscription) => subscription.ForTopic<SimpleMessage>(
                            (topic) => topic.WithName(UniqueName).WithReadConfiguration(
                                (config) => config.WithInstancePosition(1))));
                })
                .AddSingleton(handler);

            // Act and Assert
            await AssertMessagePublishedAndReceivedAsync(services, handler, completionSource);
        }

        private async Task AssertMessagePublishedAndReceivedAsync<T>(
            IServiceCollection services,
            IHandlerAsync<T> handler,
            TaskCompletionSource<object> completionSource)
            where T : Message
        {
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            IMessagePublisher publisher = serviceProvider.GetRequiredService<IMessagePublisher>();
            IMessagingBus listener = serviceProvider.GetRequiredService<IMessagingBus>();

            using (var source = new CancellationTokenSource(Timeout))
            {
                listener.Start(source.Token);

                var message = new SimpleMessage();

                // Act
                await publisher.PublishAsync(message, source.Token);

                // Assert
                completionSource.Task.Wait(source.Token);

                await handler.Received(1).Handle(Arg.Any<T>());
            }
        }
    }
}
