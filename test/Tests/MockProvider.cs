// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AspNetCore.DataProtection.Aws.Tests;

public class MockDescriptor : ServiceDescriptor
{
    protected MockProvider _mockProvider;
    private Mock _mock = null;

    private Mock CreateMock()
    {
        var mockType = typeof(Mock<>);

        mockType = mockType.MakeGenericType(ServiceType);
        return Activator.CreateInstance(mockType, _mockProvider.GetConstructorParameters(ServiceType)) as Mock;
    }

    public MockDescriptor(Type serviceType, MockProvider provider)
        : base(serviceType, serviceType, ServiceLifetime.Transient)
    {
        _mockProvider = provider;
    }

    public MockDescriptor(Type serviceType, Mock instance, MockProvider provider)
        : base(serviceType, serviceType, ServiceLifetime.Transient)
    {
        _mockProvider = provider;
        _mock = instance;
    }

    public virtual object Instance => Mock.Object;

    public virtual Mock Mock
    {
        get
        {
            if(_mock == null)
            {
                Interlocked.CompareExchange<Mock>(ref _mock, CreateMock(), null);
            }

            return _mock;
        }
    }

    public virtual void Verify()
    {
        _mock?.Verify();
    }
}

public class InstanceDescriptor : MockDescriptor
{
    public InstanceDescriptor(Type serviceType, object instance)
        : base(serviceType, null)
    {
        Instance = instance;
    }

    public override object Instance { get; }
    public override Mock Mock => null;
    public override void Verify() {}
}

public class MockProvider : IList<ServiceDescriptor>, IServiceProvider, IServiceCollection
{
    private readonly List<MockDescriptor> _mocks = new List<MockDescriptor>();
    private static readonly Type EnumerableInterfaceType = typeof(IEnumerable<>);

    public MockProvider()
    {
        Initialize();
    }

    private void Initialize()
    {
        _mocks.Add(new InstanceDescriptor(typeof(IServiceProvider), this));
        _mocks.Add(new InstanceDescriptor(typeof(IServiceCollection), this));
    }

    public MockProvider(IEnumerable<Mock> mocks)
        : this()
    {
        foreach(var mock in mocks)
        {
            var t = mock.GetType().GenericTypeArguments.First();
            Add(t, mock);
        }
    }

    public MockProvider(params Mock[] mocks)
        : this(mocks.AsEnumerable())
    {
    }

    public object GetService(Type serviceType)
    {
        //find direct type
        var retVal = _mocks.FirstOrDefault(x => x.ServiceType.FullName == serviceType.FullName);
        if(retVal != null)
            return retVal.Instance;

        if(_mocks.Any(x => x.ServiceType.Name == serviceType.Name))
        {
            return CreateMock(serviceType).Instance;
        }

        if(serviceType.Name != EnumerableInterfaceType.Name || !serviceType.IsGenericType)
        {
            return null;
        }

        var type = serviceType.GetGenericArguments().FirstOrDefault();
        if(type == null)
        {
            return null;
        }

        var arr = _mocks.Where(x => x.ServiceType.FullName == type.FullName).Select(x => x.Instance).ToArray();
        var array = Array.CreateInstance(type, arr.Length);
        for(int i = 0; i < arr.Length; ++i)
        {
            array.SetValue(arr[i], i);
        }

        return array;

    }

    public Mock<T> GetMock<T>() where T : class => GetMock(typeof(T)) as Mock<T>;

    public Mock GetMock(Type serviceType)
    {
        var retVal = _mocks.FirstOrDefault(x => x.ServiceType.FullName == serviceType.FullName);
        if(retVal == null && _mocks.Any(x => x.ServiceType.Name == serviceType.Name))
        {
            return CreateMock(serviceType).Mock;
        }

        return retVal?.Mock;
    }

    public void Add<T>(Mock<T> mock) where T : class
    {
        Add(typeof(T), mock);
    }

    public MockDescriptor Add(Type t, Mock mock)
    {
        var e = new MockDescriptor(t, mock, this);
        _mocks.Add(e);
        return e;
    }

    public Mock<U> CreateMock<U>(params object[] o) where U : class
    {
        return CreateMock(typeof(U), o).Mock as Mock<U>;
    }

    public Mock<U> CreateMock<U>() where U : class
    {
        return CreateMock(typeof(U)).Mock as Mock<U>;
    }

    public MockDescriptor CreateMock(Type serviceType) => CreateMock(serviceType, GetConstructorParameters(serviceType));

    public MockDescriptor CreateMock(Type serviceType, params object[] o) => CreateMock(serviceType, o.AsEnumerable());

    public MockDescriptor CreateMock(Type serviceType, IEnumerable<object> o)
    {
        var mockType = typeof(Mock<>);

        mockType = mockType.MakeGenericType(serviceType);
        Mock m = Activator.CreateInstance(mockType, o) as Mock;
        return Add(serviceType, m);
    }

    public object[] GetConstructorParameters(Type t)
    {
        var c = t.GetConstructors().FirstOrDefault(x => !x.GetParameters().Any() || x.GetParameters().All(y => _mocks.Any(xz => xz.ServiceType.Name == y.ParameterType.Name)));
        if(c != null && c.GetParameters().Any())
        {
            var parameters = c.GetParameters();
            return parameters.Select(p => GetService(p.ParameterType)).ToArray();
        }
        else
            return new object[] {};
    }

    public void Verify()
    {
        foreach(var m in _mocks)
            m.Verify();
    }

    public int Count => _mocks.Count;

    public bool IsReadOnly => false;

    public ServiceDescriptor this[int index]
    {
        get => _mocks[index];
        set => _mocks[index] = value as MockDescriptor ?? new MockDescriptor(value.ServiceType, this);
    }

    public void Add(ServiceDescriptor item)
    {
        _mocks.Add(item as MockDescriptor ?? new MockDescriptor(item.ServiceType, this));
    }

    public int IndexOf(ServiceDescriptor item)
    {
        var mock = _mocks.FirstOrDefault(x => x.ServiceType.FullName == item.ServiceType.FullName);
        return mock == null ? -1 : _mocks.IndexOf(mock);
    }

    public void Insert(int index, ServiceDescriptor item)
    {
        _mocks.Insert(index, item as MockDescriptor ?? new MockDescriptor(item.ServiceType, this));
    }

    public void RemoveAt(int index)
    {
        _mocks.RemoveAt(index);
    }

    public void Clear()
    {
        _mocks.Clear();
        Initialize();
    }

    public bool Contains(ServiceDescriptor item) => _mocks.Any(x => x.ServiceType.FullName == item.ServiceType.FullName);

    public void CopyTo(ServiceDescriptor[] array, int arrayIndex)
    {
        _mocks.Select(x => x as ServiceDescriptor).ToArray().CopyTo(array, arrayIndex);
    }

    public bool Remove(ServiceDescriptor item)
    {
        var mock = _mocks.FirstOrDefault(x => x.ServiceType.FullName == item.ServiceType.FullName);
        return (mock != null) && _mocks.Remove(mock);
    }

    public IEnumerator<ServiceDescriptor> GetEnumerator() => _mocks.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class IocTesting
{
    private static List<Type> FromInterface(Assembly asm, Type tBase)
        => asm.GetTypes().Where(x => x.GetInterfaces().Contains(tBase)).ToList();

    private static List<Type> FromClass(Assembly asm, Type tBase)
        => asm.GetTypes().Where(x => x.IsSubclassOf(tBase)).ToList();

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="TBase">The Base Class or Interface, every class to test has in common</typeparam>
    /// <param name="configureServices">populate the mockprovider in this method</param>
    /// <param name="asm">optional assembly, if not provided it is taken from the assembly where "configureServices" is taken from</param>
    /// <returns>Missing Type Registrations, with infos about Type, Method and Parameter names</returns>
    public static IEnumerable<MissingTypeRegistration> FindMissingRegistrations<TBase>(Action<IServiceCollection> configureServices, Assembly asm = null)
    {
        var missing = new LinkedList<MissingTypeRegistration>();
        var mocks = new MockProvider();
        var tBase = typeof(TBase);

        asm = asm ?? configureServices.Method.DeclaringType.Assembly;
        List<Type> types = tBase.IsInterface ? FromInterface(asm, tBase) : FromClass(asm, tBase);

        configureServices(mocks);
        foreach(var t in types)
        {
            foreach(var c in t.GetConstructors())
            {
                var parameters = c.GetParameters();
                foreach(var p in parameters)
                {
                    if(mocks.GetService(p.ParameterType) == null)
                        missing.AddLast(new MissingTypeRegistration(t, c, p));
                }
            }

            foreach(var m in t.GetMethods())
            {
                var parameters = m.GetParameters().Where(x => x.GetCustomAttributes().Any(xz => xz.GetType().FullName == "Microsoft.AspNetCore.Mvc.FromServicesAttribute"));
                foreach(var p in parameters)
                {
                    if(mocks.GetService(p.ParameterType) == null)
                        missing.AddLast(new MissingTypeRegistration(t, m, p));
                }
            }
        }

        return missing;
    }

    public class MissingTypeRegistration
    {
        public MissingTypeRegistration(Type type, MemberInfo method, ParameterInfo parameter)
        {
            Type = type;
            Method = method;
            Parameter = parameter;
        }

        public Type Type { get; }
        public MemberInfo Method { get; }
        public ParameterInfo Parameter { get; }

        public override string ToString() => $"{Type.FullName} -> {Method.Name} -> {Parameter.Name}";
    }
}
