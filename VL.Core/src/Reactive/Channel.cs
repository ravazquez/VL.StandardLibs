﻿using Stride.Core;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using VL.Core;

#nullable enable

namespace VL.Lib.Reactive
{
    public interface IChannel
    {
        Type ClrTypeOfValues { get; }
        ImmutableList<object> Components { get; set; }
        IChannel<object> ChannelOfObject { get; }
        bool Enabled { get; set; }
        bool IsBusy { get; }
        object? Object { get; set; }
        string? LatestAuthor { get; }
        void SetObjectAndAuthor(object? @object, string? author);
    }

    [Monadic(typeof(Monadic.ChannelFactory<>))]
    public interface IChannel<T> : IChannel, ISubject<T?>, IDisposable
    {
        public T? Value { get; set; }
        void SetValueAndAuthor(T? value, string? author);
    }

    internal abstract class C<T> : IChannel<T>, ISwappableGenericType
    {
        protected readonly Subject<T?> subject = new();

        public ImmutableList<object> Components { get; set; } = ImmutableList<object>.Empty;

        object ISwappableGenericType.Swap(Type newType, Swapper swapObject)
        {
            var arg = newType.GenericTypeArguments[0];
            var channel = ChannelHelpers.CreateChannelOfType(arg);
            if (channel is not null)
                channel.SetObjectAndAuthor(swapObject(Value, arg), LatestAuthor);
#nullable disable
            return channel;
#nullable enable
        }

        const int maxStack = 1;
        int stack;

        public bool IsBusy => stack > 0;

        T? value = default;
        public T? Value
        {
            get
            {
                return value;
            }
            set
            {
                SetValueAndAuthor(value, null);
            }
        }

        public void SetValueAndAuthor(T? value, string? author)
        {
            AssertAlive();
            if (!Enabled || !this.IsValid())
                return;

            LatestAuthor = author;
            this.value = value;

            if (stack < maxStack)
            {
                stack++;
                try
                {
                    subject.OnNext(value);
                }
                finally
                {
                    stack--;
                }
            }
        }

        object? IChannel.Object { get => Value; set => Value = (T?)value; }

        void IChannel.SetObjectAndAuthor(object? @object, string? author)
        {
            SetValueAndAuthor((T?)@object, author);
        }

        IChannel<object> IChannel.ChannelOfObject => channelOfObject;

        public Type ClrTypeOfValues => typeof(T);


        protected abstract IChannel<object> channelOfObject {get;}

        void IObserver<T?>.OnCompleted()
        {
            AssertAlive();
            if (Enabled)
                subject.OnCompleted();
        }

        void IObserver<T?>.OnError(Exception error)
        {
            AssertAlive();
            if (Enabled)
                subject.OnError(error);
        }

        void IObserver<T?>.OnNext(T? value)
        {
            AssertAlive();
            SetValueAndAuthor(value, null);
        }

        IDisposable IObservable<T?>.Subscribe(IObserver<T?> observer)
        {
            AssertAlive();
            if (subject.IsDisposed) 
                return Disposable.Empty;                
            return subject.Subscribe(observer);
        }

        protected void AssertAlive()
        {
            Debug.Assert(!subject.IsDisposed, "you work with a disposed channel!");
        }

        public bool Enabled { get; set; } = true;

        public string? LatestAuthor { get; set; }

        bool disposing = false;
        void IDisposable.Dispose()
        {
            if (disposing)
                return;

            AssertAlive();
            disposing = true;
            try
            {
                foreach (var c in Components)
                    (c as IDisposable)?.Dispose();
                Enabled = false;
                subject.Dispose();
            }
            finally
            {
                disposing = false;
            }
        }
    }

    internal class Channel<T> : C<T>, IChannel<object>
    {
        protected override IChannel<object> channelOfObject => this;

        object? IChannel<object>.Value { get => Value; set { Value = (T?)value; } }
        
        void IChannel<object>.SetValueAndAuthor(object? value, string? author)
        {
            SetValueAndAuthor((T?)value, author);
        }

        void IObserver<object?>.OnCompleted()
        {
            AssertAlive();
            if (Enabled)
                subject.OnCompleted();
        }

        void IObserver<object?>.OnError(Exception error)
        {
            AssertAlive();
            if (Enabled)
                subject.OnError(error);
        }

        void IObserver<object?>.OnNext(object? value)
        {
            Value = (T?)value;
        }
        
        IDisposable IObservable<object?>.Subscribe(IObserver<object?> observer)
        {
            AssertAlive();
            if (subject.IsDisposed)
                return Disposable.Empty;
            if (observer is IObserver<T?> obsT)
                return subject.Subscribe(obsT);
            return subject.Subscribe(v => observer.OnNext(v), e => observer.OnError(e), () => observer.OnCompleted());
        }

        public static implicit operator T?(Channel<T> c) => c.Value;
    }


    public static class DummyChannelHelpers<T>
    {
        public static readonly IChannel<T> Instance; 
        
        static DummyChannelHelpers()
        {
            Instance = new DummyChannel<T>();
            Instance.Value = TypeUtils.Default<T>();
            Instance.Enabled = false;
        }
    }

    interface IDummyChannel { }

    internal sealed class DummyChannel<T> : Channel<T>, IDummyChannel
    {
    }

    public static class ChannelHelpers
    {
        public static void AddComponent(this IChannel channel, object component)
        {
            channel.Components = channel.Components.Add(component);
        }

        public static void RemoveComponent(this IChannel channel, object component)
        {
            channel.Components = channel.Components.Remove(component);
            //(component as IDisposable)?.Dispose();
        }

        public static TComponent? TryGetComponent<TComponent>(this IChannel channel) where TComponent : class
            => channel.Components.OfType<TComponent>().FirstOrDefault();

        public static TComponent EnsureSingleComponentOfType<TComponent>(this IChannel channel, Func<TComponent> producer, bool renew) where TComponent : class
        {
            var c = channel.TryGetComponent<TComponent>();
            if (c is null)
            {
                c = producer();
                channel.Components = channel.Components.Add(c);
                return c;
            }

            if (!renew)
                return c;

            var newC = producer();
            channel.Components = channel.Components.Replace(c, newC);
            (c as IDisposable)?.Dispose();
            return newC;
        }

        public static object EnsureSingleComponentOfType(this IChannel channel, object component, bool renew)
        {
            var type = component.GetType();
            foreach (object? c in channel.Components)
            {
                if (c.GetType() == type)
                {
                    if (!renew)
                    {
                        (component as IDisposable)?.Dispose();
                        return c;
                    }
                    channel.Components = channel.Components.Replace(c, component);
                    (c as IDisposable)?.Dispose();
                    return component;
                }
            }
            channel.Components = channel.Components.Add(component);
            return component;
        }

        public static IChannel<IReadOnlyCollection<Attribute>> Attributes(this IChannel channel)
            => channel.EnsureSingleComponentOfType(() =>
                {
                    var c = CreateChannelOfType<IReadOnlyCollection<Attribute>>();
                    c.Value = Array.Empty<Attribute>();
                    return c;
                }, false);

        public static IChannel<T> CreateChannelOfType<T>()
        {
            return new Channel<T>();
        }
        public static IChannel<object> CreateChannelOfType(Type typeOfValues)
        {
            return (IChannel<object>)Activator.CreateInstance(typeof(Channel<>).MakeGenericType(typeOfValues))!;
        }
        public static IChannel<object> CreateChannelOfType(IVLTypeInfo typeOfValues)
        {
            return (IChannel<object>)Activator.CreateInstance(typeof(Channel<>).MakeGenericType(typeOfValues.ClrType))!;
        }

        public static bool IsValid([NotNullWhen(true)] this IChannel? c)
            => c is not null && c is not IDummyChannel;

        public static void EnsureValue<T>(this IChannel<T> input, T? value, bool force = false)
        {
            if (force || !EqualityComparer<T>.Default.Equals(input.Value, value))
                input.Value = value;
        }

        public static void EnsureObject(this IChannel input, object? value, bool force = false)
        {
            if (force || !EqualityComparer<object>.Default.Equals(input.Object, value))
                input.Object = value;
        }

        public static IDisposable Merge<T>(this IChannel<T> a, IChannel<T> b, ChannelMergeInitialization initialization, ChannelSelection pushEagerlyTo)
        {
            return Merge(a, b, v => v, v => v, initialization, pushEagerlyTo);
        }

        public static IDisposable Merge<A, B>(this IChannel<A> a, IChannel<B> b, Func<A?, B?> toB, Func<B?, A?> toA, ChannelMergeInitialization initialization, ChannelSelection pushEagerlyTo)
        {
            var subscription = new CompositeDisposable();

            switch (initialization)
            {
                case ChannelMergeInitialization.UseA:
                    b.EnsureValue(toB(a.Value));
                    break;
                case ChannelMergeInitialization.UseB:
                    a.EnsureValue(toA(b.Value));
                    break;
            }

            var isBusy = false;
            subscription.Add(a.Subscribe(v =>
            {
                if (!isBusy)
                {
                    isBusy = true;
                    try
                    {
                        b.EnsureValue(toB(v), pushEagerlyTo.HasFlag(ChannelSelection.ChannelB));
                    }
                    finally
                    {
                        isBusy = false;
                    }
                }
            }));
            subscription.Add(b.Subscribe(v =>
            {
                if (!isBusy)
                {
                    isBusy = true;
                    try
                    {
                        a.EnsureValue(toA(v), pushEagerlyTo.HasFlag(ChannelSelection.ChannelA));
                    }
                    finally
                    {
                        isBusy = false;
                    }
                }
            }));

            return subscription;
        }

        public static IDisposable Merge<A, B>(this IChannel<A> a, IChannel<B> b, Func<A?, Optional<B>> toB, Func<B?, Optional<A>> toA, ChannelMergeInitialization initialization, ChannelSelection pushEagerlyTo)
        {
            var subscription = new CompositeDisposable();

            switch (initialization)
            {
                case ChannelMergeInitialization.UseA:
                {
                    var optionalV = toB(a.Value);
                    if (optionalV.HasValue)
                        b.EnsureValue(optionalV.Value);
                    break;
                }
                case ChannelMergeInitialization.UseB:
                {
                    var optionalV = toA(b.Value);
                    if (optionalV.HasValue)
                        a.EnsureValue(optionalV.Value);
                    break;
                }
            }

            var isBusy = false;
            subscription.Add(a.Subscribe(v =>
            {
                if (!isBusy)
                {
                    isBusy = true;
                    try
                    {
                        var x = toB(v);
                        if (x.HasValue)
                            b.EnsureValue(x.Value, pushEagerlyTo.HasFlag(ChannelSelection.ChannelB));
                    }
                    finally
                    {
                        isBusy = false;
                    }
                }
            }));
            subscription.Add(b.Subscribe(v =>
            {
                if (!isBusy)
                {
                    isBusy = true;
                    try
                    {
                        var x = toA(v);
                        if (x.HasValue)
                            a.EnsureValue(x.Value, pushEagerlyTo.HasFlag(ChannelSelection.ChannelA));
                    }
                    finally
                    {
                        isBusy = false;
                    }
                }
            }));

            return subscription;
        }
    }

}
#nullable disable
