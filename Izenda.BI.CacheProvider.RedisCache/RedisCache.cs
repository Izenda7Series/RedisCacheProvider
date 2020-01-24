using Izenda.BI.CacheProvider.RedisCache.Utilities;
using log4net;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Linq;

namespace Izenda.BI.CacheProvider.RedisCache
{
    /// <summary>
    /// The Redis Cache
    /// </summary>
    public class RedisCache
    {
        private readonly IDatabase cache;
        private readonly IServer server;
        private readonly JsonSerializerSettings serializerSettings;
        private readonly JsonSerializer serializer;
        private readonly ILog logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisCache"/> class
        /// </summary>
        /// <param name="serializerSettings">The serializer settings</param>
        public RedisCache(JsonSerializerSettings serializerSettings)
        {
            cache = RedisHelper.Database;
            server = RedisHelper.Server;
            this.serializerSettings = serializerSettings;
            serializer = JsonSerializer.Create(serializerSettings);
            logger = LogManager.GetLogger(this.GetType());
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisCache"/> class
        /// </summary>
        /// <param name="cache">The redis database cache</param>
        /// <param name="serializerSettings">The serializer settings</param>
        public RedisCache(IDatabase cache, JsonSerializerSettings serializerSettings)
        {
            this.cache = cache;
            this.serializerSettings = serializerSettings;
            serializer = JsonSerializer.Create(serializerSettings);
            logger = LogManager.GetLogger(this.GetType());
        }

        /// <summary>
        /// Gets the key's value
        /// </summary>
        /// <typeparam name="T">The type of the value</typeparam>
        /// <param name="key">The key</param>
        /// <returns>The value</returns>
        public T Get<T>(string key)
        {
            try
            {
                var result = cache.StringGet(key);
                if (result.IsNullOrEmpty)
                    return default;

                return this.Deserialize<T>(result);
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during GET of key, {key}", ex);
                return default;
            }
        }

        /// <summary>
        /// Gets the key's value
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="type">The type of the value</param>
        /// <returns>The value</returns>
        public object Get(string key, Type type)
        {
            try
            {
                var result = cache.StringGet(key);
                if (result.IsNullOrEmpty)
                    return default;

                return this.Deserialize(result, type);
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during GET of key, {key}", ex);
                return default;
            }
        }

        /// <summary>
        /// Sets the key and it's value
        /// </summary>
        /// <typeparam name="T">The type of the value</typeparam>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        public void Set<T>(string key, T value)
        {
            try
            {
                var json = this.Serialize(value);
                cache.StringSet(key, json);
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during SET of key, {key}", ex);
            }
        }

        /// <summary>
        /// Sets the key and it's value with a lifetime
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        /// <param name="expiration">The lifetime expiration</param>
        public void SetWithLifetime(string key, object value, TimeSpan expiration)
        {
            try
            {
                var json = this.Serialize(value);
                cache.StringSet(key, json, expiration);
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during SET with lifetime of key, {key}", ex);
            }
        }

        /// <summary>
        /// Checks if the cache contains the key
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns>Whether or not the key is present</returns>
        public bool Contains(string key)
        {
            try
            {
                return cache.KeyExists(key);
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during CONTAINS of key, {key}", ex);
                return false;
            }
        }

        /// <summary>
        /// Removes the key
        /// </summary>
        /// <param name="key">The key</param>
        public void Remove(string key)
        {
            try
            {
                cache.KeyDelete(key);
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during REMOVE of key, {key}", ex);
            }
        }

        /// <summary>
        /// Removes any key that matches the pattern
        /// </summary>
        /// <param name="pattern">The pattern</param>
        public void RemoveWithPattern(string pattern)
        {
            try
            {
                var keysToRemove = server.Keys(cache.Database, $"*{pattern}*").ToArray();
                foreach (var key in keysToRemove)
                {
                    cache.KeyDelete(key);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Redis Cache error occured during REMOVE of keys with pattern, {pattern}", ex);
            }
        }

        /// <summary>
        /// Serializes the object
        /// </summary>
        /// <param name="value">The value</param>
        /// <returns>The serialized string</returns>
        private string Serialize(object value)
        {
            var json = JsonConvert.SerializeObject(value, serializerSettings);
            return json.Compress();
        }

        /// <summary>
        /// Deserializes the string
        /// </summary>
        /// <typeparam name="T">The type of the outputted object</typeparam>
        /// <param name="serialized">The serialized string</param>
        /// <returns>The outputted and deserialized object</returns>
        private T Deserialize<T>(string serialized)
        {
            using (var stream = serialized.DecompressToStreamReader())
            using (JsonReader reader = new JsonTextReader(stream))
            {
                return serializer.Deserialize<T>(reader);
            }
        }

        /// <summary>
        /// Deserializes the string
        /// </summary>
        /// <param name="serialized">The serialized string</param>
        /// <param name="type">he type of the outputted object</param>
        /// <returns>The outputted and deserialized object</returns>
        private object Deserialize(string serialized, Type type)
        {
            using (var stream = serialized.DecompressToStreamReader())
            using (JsonReader reader = new JsonTextReader(stream))
            {
                return serializer.Deserialize(reader, type);
            }
        }
    }
}