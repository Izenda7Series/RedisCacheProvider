using Izenda.BI.CacheProvider.RedisCache.Constants;
using Izenda.BI.CacheProvider.RedisCache.Converters;
using Izenda.BI.CacheProvider.RedisCache.Extensions;
using Izenda.BI.CacheProvider.RedisCache.Resolvers;
using Izenda.BI.Framework.Models.ReportDesigner;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Izenda.BI.CacheProvider.RedisCache
{
    /// <summary>
    /// Redis cache provider
    /// </summary>
#warning The current version of this project will only work with Izenda versions 2.4.4+
    [Export(typeof(ICacheProvider))]
    public class RedisCacheProvider : ICacheProvider, IDisposable
    {
        private bool _disposed = false;
        private bool _enableValueCompression = true;
        private JsonSerializer _serializer;
        private JsonSerializerSettings _serializerSettings = new JsonSerializerSettings();
        private readonly IDatabase _cache;
        private readonly IServer _server;

        public RedisCacheProvider()
        {
            _cache = RedisHelper.Database;
            _server = RedisHelper.Server;
            InitSerializer();
        }

        public RedisCacheProvider(IDatabase cache)
        {
            _cache = cache;
            InitSerializer();
        }

        /// <summary>
        /// Initializes the JSON serializer
        /// </summary>
        private void InitSerializer()
        {
            var resolver = new IzendaSerializerContractResolver();
            resolver.Ignore(typeof(ReportPartDefinition), "ReportPartContent");

            _serializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            _serializerSettings.TypeNameHandling = TypeNameHandling.Objects;
            _serializerSettings.TypeNameAssemblyFormat = System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple;

            _serializerSettings.Converters.Add(new DBServerTypeSupportingConverter());
            _serializerSettings.ContractResolver = resolver;

            _serializer = JsonSerializer.Create(_serializerSettings);
        }

        /// <summary>
        /// Serializes the obj to json
        /// </summary>
        /// <param name="obj"></param>
        /// <returns> A json string of the object</returns>
        private string Serialize(object obj)
        {
            string value = JsonConvert.SerializeObject(obj, _serializerSettings);

            if (_enableValueCompression)
            {
                value = value.Compress();
            }

            return value;
        }

        /// <summary>
        /// Deserializes the json string to the specified type
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="serialized">The serialized object</param>
        /// <returns>THe deserialized object</returns>
        private T Deserialize<T>(string serialized)
        {
            if (_enableValueCompression)
            {
                using (StreamReader stream = serialized.DecompressToStreamReader())
                using (JsonReader reader = new JsonTextReader(stream))
                {
                    try
                    {
                        return _serializer.Deserialize<T>(reader);
                    }
                    catch (Exception ex)
                    {
                        Trace.Write(string.Format(AppConstants.ExceptionTemplate, ex.ToString()));
                    }
                }
            }

            return JsonConvert.DeserializeObject<T>(serialized, _serializerSettings);
        }

        /// <summary>
        /// Adds an item to the cache using the specified key.
        /// </summary>
        /// <param name="key"> The key </param>
        /// <param name="value"> The value </param>
        public void Add<T>(string key, T value)
        {
            try
            {
                _cache.StringSet(key, Serialize(value));
            }
            catch (Exception ex)
            {
                Trace.Write(string.Format(AppConstants.ExceptionTemplate, ex.ToString()));
            }
        }

        /// <summary>
        /// Adds an item to the cache using the specified key and sets an expiration
        /// </summary>
        /// <param name="key"> The key </param>
        /// <param name="value"> The value</param>
        /// <param name="expiration"> The expiration </param>
        public void AddWithExactLifetime(string key, object value, TimeSpan expiration)
        {  
            try
            {
                _cache.StringSet(key, Serialize(value), expiration);
            }
            catch (Exception ex)
            {
                Trace.Write(string.Format(AppConstants.ExceptionTemplate, ex.ToString()));
            }
        }

        /// <summary>
        /// Adds an item to the cache using the specified key and sets a sliding expiration
        /// </summary>
        /// <param name="key"> The key </param>
        /// <param name="value"> The value</param>
        /// <param name="expiration"> The expiration </param>
        public void AddWithSlidingLifetime(string key, object value, TimeSpan expiration)
        {
            AddWithExactLifetime(key, value, expiration);
        }

        /// <summary>
        /// Checks if the cache contains the given key
        /// </summary>
        /// <param name="key"> The key</param>
        /// <returns>true if the cache contains the key, false otherwise</returns>
        public bool Contains(string key)
        {
            return _cache.KeyExists(key);
        }

        /// <summary>
        /// Retrieves the specified key from the cache
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="key">The key</param>
        /// <returns></returns>
        public T Get<T>(string key)
        {            
            var result = _cache.StringGet(key);
            if (result.IsNullOrEmpty)
                return default(T);

            return Deserialize<T>(result);
        }

        /// <summary>
        /// Removes the specified item from the cache.
        /// </summary>
        /// <param name="key">The key</param>
        public void Remove(string key)
        {
            RemoveKeyWithPattern(key);
        }

        /// <summary>
        /// Removes the keys matching the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern. </param>
        public void RemoveKeyWithPattern(string pattern)
        {
            pattern = pattern.StartsWith("ReportCacheKey") ? "_Report_Report_" : pattern;
            var keysToRemove = _server.Keys(_cache.Database, pattern + "*").ToArray();

            try
            {
                foreach (var key in keysToRemove)
                {
                    _cache.KeyDelete(key);
                }
            }
            catch (Exception ex)
            {
                Trace.Write(string.Format(AppConstants.ExceptionTemplate, ex.ToString()));
            }
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="key">The key</param>
        /// <param name="executor">The function call that returns the data.</param>
        /// <returns></returns>
        public T Ensure<T>(string key, Func<T> executor)
        {
            return EnsureCache(executor, key, TimeSpan.Zero, (cacheKey, result, expiration) =>
            {
                Add(cacheKey, result);
            });
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The type to convert the object to.</typeparam>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="expiration"> The expiration </param>
        /// <param name="executor">The function call that returns the data.</param>
        public T EnsureWithExactLifetime<T>(string key, TimeSpan expiration, Func<T> executor)
        {
            if (key.Contains("_Report_Report_"))
            {
                expiration = TimeSpan.FromSeconds(1000);
            }

            return EnsureCache(executor, key, expiration, (cacheKey, result, expirationTime) =>
            {
                AddWithExactLifetime(cacheKey, result, expirationTime);
            });
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="expiration"> The sliding expiration </param>
        /// <param name="executor">The function call that returns the data.</param>
        public T EnsureWithSlidingLifetime<T>(string key, TimeSpan expiration, Func<T> executor)
        {
            return EnsureWithExactLifetime<T>(key, expiration, executor);
        }

        /// <summary>
        /// Update the cache with the specified value, if the cache does not exist, it is created.
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="key">The key.</param>
        /// <param name="expiration">The expiration timeout as a timespan. This is a sliding value.</param>
        /// <param name="executor">The function call that returns the data.</param>
        public T UpdateWithSlidingLifetime<T>(string key, TimeSpan expiration, Func<T> executor)
        {
            var newValue = executor();

            try
            {
                if (newValue != null)
                {
                    _cache.StringSet(key, Serialize(newValue), expiration);
                }
            }
            catch (Exception ex)
            {
                Trace.Write(string.Format(AppConstants.ExceptionTemplate, ex.ToString()));
            }
 
            return newValue;
        }

        /// <summary>
        /// Retrieves the specified key from the cache. If no value exists, a cache entry is created. 
        /// </summary>
        /// <typeparam name="T">The type to convert the object to.</typeparam>
        /// <param name="executor">The function call that returns the data.</param>
        /// <param name="key">The key.</param>
        /// <param name="expiration">The expiration timeout as a timespan.</param>
        private T EnsureCache<T>(Func<T> executor, string key, TimeSpan expiration, Action<string, T, TimeSpan> addItemToCache)
        {
            var result = Get<T>(key);

            if (EqualityComparer<T>.Default.Equals(result, default(T)))
            {               
                try
                {
                    result = Get<T>(key);

                    if (EqualityComparer<T>.Default.Equals(result, default(T)))
                    {
                        var newValue = executor();

                        result = newValue;
                    }

                    if (result != null)
                    {
                        addItemToCache(key, result, expiration);
                    }
                }
                catch (Exception ex)
                {
                    Trace.Write(string.Format(AppConstants.ExceptionTemplate, ex.ToString()));
                }
            }

            return result;
        }

        /// <summary>
        /// Dispose object
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose object
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
            }

            _disposed = true;
        }

        /// <summary>
        /// Dispose object
        /// </summary>
        ~RedisCacheProvider()
        {
            Dispose(false);
        }
    }
}
