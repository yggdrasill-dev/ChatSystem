using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Configuration;

public class MessageQueueConfiguration
{
	private readonly List<ISubscribeRegistration> m_SubscribeRegistrations = [];

	public IServiceCollection Services { get; }

	internal Action<MessageQueueOptions, IServiceProvider> OptionsConfigure { get; private set; } =
		(options, sp) => { };

	public MessageQueueConfiguration(IServiceCollection services)
	{
		Services = services;

		Services.AddSingleton<IEnumerable<ISubscribeRegistration>>(m_SubscribeRegistrations);
	}

	public MessageQueueConfiguration AddHandler<THandler>(string subject) where THandler : IMessageHandler
	{
		m_SubscribeRegistrations.Add(new SubscribeRegistration<THandler>(subject));

		return this;
	}

	public MessageQueueConfiguration AddHandler<THandler>(string subject, string group) where THandler : IMessageHandler
	{
		m_SubscribeRegistrations.Add(new GroupRegistration<THandler>(subject, group));

		return this;
	}

	public MessageQueueConfiguration ConfigQueueOptions(Action<MessageQueueOptions, IServiceProvider> configure)
	{
		OptionsConfigure = configure;

		return this;
	}
}
