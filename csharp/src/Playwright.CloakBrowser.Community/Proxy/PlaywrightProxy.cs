using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Playwright.CloakBrowser.Community.Proxy.Interceptors;

namespace Playwright.CloakBrowser.Community.Proxy
{
    public class PlaywrightProxy<T> : DispatchProxy where T : class
    {
        private T _target = null!;
        private object _interceptor = null!;

        public static T Create(T target, object interceptor)
        {
            object proxy = Create<T, PlaywrightProxy<T>>();
            ((PlaywrightProxy<T>)proxy)._target = target;
            ((PlaywrightProxy<T>)proxy)._interceptor = interceptor;
            return (T)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) return null;
            args ??= Array.Empty<object>();

            // 1. Check if there is an exact or matching signature method on the interceptor.
            // Match by name and parameter types.
            var paramTypes = targetMethod.GetParameters().Select(p => p.ParameterType).ToArray();
            var interceptorMethod = _interceptor.GetType().GetMethod(
                targetMethod.Name,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                paramTypes,
                null
            );

            if (interceptorMethod != null)
            {
                try
                {
                    var interceptedResult = interceptorMethod.Invoke(_interceptor, args);
                    return interceptedResult;
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            }

            // 2. Default fallback: execute on target
            try
            {
                var result = targetMethod.Invoke(_target, args);
                if (result == null) return null;

                return WrapResult(result, targetMethod.ReturnType, _interceptor);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static object WrapResult(object result, Type returnType, object interceptor)
        {
            // Handle Task<TResult>
            if (result is Task task)
            {
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var genericArg = returnType.GetGenericArguments()[0];
                    var wrapMethod = typeof(PlaywrightProxy<T>)
                        .GetMethod(nameof(WrapTaskResultAsync), BindingFlags.NonPublic | BindingFlags.Static)?
                        .MakeGenericMethod(genericArg);
                    if (wrapMethod != null)
                    {
                        return wrapMethod.Invoke(null, new object[] { task, interceptor })!;
                    }
                }
                return result;
            }

            // Handle direct synchronous values
            return WrapValue(result, interceptor);
        }

        private static object WrapValue(object val, object interceptor)
        {
            if (val == null) return null!;

            if (val is IBrowser browser)
            {
                if (interceptor is BrowserInterceptor bi)
                    return PlaywrightProxy<IBrowser>.Create(browser, bi);
                return PlaywrightProxy<IBrowser>.Create(browser, interceptor);
            }
            if (val is IBrowserContext context)
            {
                if (interceptor is BrowserInterceptor bi)
                    return PlaywrightProxy<IBrowserContext>.Create(context, new ContextInterceptor(context, bi.Options));
                if (interceptor is ContextInterceptor ci)
                    return PlaywrightProxy<IBrowserContext>.Create(context, ci);
                return PlaywrightProxy<IBrowserContext>.Create(context, interceptor);
            }
            if (val is IPage page)
            {
                if (interceptor is ContextInterceptor ci)
                    return PlaywrightProxy<IPage>.Create(page, new PageInterceptor(page, ci.Options));
                if (interceptor is PageInterceptor pi)
                {
                    if (pi.Page != page)
                        return PlaywrightProxy<IPage>.Create(page, new PageInterceptor(page, pi.Options));
                    return PlaywrightProxy<IPage>.Create(page, pi);
                }
                return PlaywrightProxy<IPage>.Create(page, interceptor);
            }
            if (val is ILocator locator)
            {
                if (interceptor is PageInterceptor pi)
                    return PlaywrightProxy<ILocator>.Create(locator, new LocatorInterceptor(locator, pi.PageState, pi.RawMouse, pi.RawKeyboard));
                if (interceptor is LocatorInterceptor li)
                    return PlaywrightProxy<ILocator>.Create(locator, new LocatorInterceptor(locator, li.PageState, li.RawMouse, li.RawKeyboard));
                if (interceptor is ElementHandleInterceptor ei)
                    return PlaywrightProxy<ILocator>.Create(locator, new LocatorInterceptor(locator, ei.PageState, ei.RawMouse, ei.RawKeyboard));
                return PlaywrightProxy<ILocator>.Create(locator, interceptor);
            }
            if (val is IElementHandle handle)
            {
                if (interceptor is PageInterceptor pi)
                    return PlaywrightProxy<IElementHandle>.Create(handle, new ElementHandleInterceptor(handle, pi.Page, pi.PageState, pi.RawMouse, pi.RawKeyboard));
                if (interceptor is LocatorInterceptor li)
                    return PlaywrightProxy<IElementHandle>.Create(handle, new ElementHandleInterceptor(handle, li.Page, li.PageState, li.RawMouse, li.RawKeyboard));
                if (interceptor is ElementHandleInterceptor ei)
                    return PlaywrightProxy<IElementHandle>.Create(handle, new ElementHandleInterceptor(handle, ei.Page, ei.PageState, ei.RawMouse, ei.RawKeyboard));
                return PlaywrightProxy<IElementHandle>.Create(handle, interceptor);
            }

            // Handle collections/lists of Playwright interfaces (e.g. context.Pages)
            var type = val.GetType();
            if (val is IEnumerable && type.IsGenericType)
            {
                var itemType = type.GetGenericArguments()[0];
                if (itemType == typeof(IPage) || itemType == typeof(IBrowserContext) || itemType == typeof(IElementHandle))
                {
                    var wrappedList = new List<object>();
                    foreach (var item in (IEnumerable)val)
                    {
                        wrappedList.Add(WrapValue(item, interceptor));
                    }

                    // Cast to IReadOnlyList<TItem> or generic List
                    var castMethod = typeof(Enumerable).GetMethod("Cast")?.MakeGenericMethod(itemType);
                    var toListMethod = typeof(Enumerable).GetMethod("ToList")?.MakeGenericMethod(itemType);
                    if (castMethod != null && toListMethod != null)
                    {
                        var casted = castMethod.Invoke(null, new object[] { wrappedList });
                        return toListMethod.Invoke(null, new object[] { casted! })!;
                    }
                }
            }

            return val;
        }

        private static async Task<TResult> WrapTaskResultAsync<TResult>(Task<TResult> task, object interceptor)
        {
            var result = await task;
            return (TResult)WrapValue(result!, interceptor);
        }
    }
}

