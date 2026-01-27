using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Framework.Network;
using Google.Protobuf;

namespace UI.ViewModels
{
    // 通用的 Proto 自动 ViewModel：仅暴露协议原生属性（按需反射）
    public class ProtoAutoViewModel<T> : INotifyPropertyChanged where T : class, IMessage
    {
        private T data;
        private List<(string Label, string Value)> properties = new List<(string, string)>();

        public T Data
        {
            get => data;
            private set
            {
                if (!Equals(data, value))
                {
                    data = value;
                    RebuildProperties();
                    OnPropertyChanged(nameof(Properties));
                }
            }
        }

        public IReadOnlyList<(string Label, string Value)> Properties => properties;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Initialize()
        {
            Data = ProtobufManager.Instance.GetData<T>();
            ProtobufManager.Instance.OnDataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(string typeName, object obj)
        {
            if (typeName == typeof(T).Name && obj is T typed)
            {
                Data = typed;
            }
            else if (typeName == typeof(T).Name && obj is IMessage msg)
            {
                Data = msg as T;
            }
        }

        public void Dispose()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnDataUpdated;
        }

        private void RebuildProperties()
        {
            properties.Clear();
            if (Data == null) return;

            var t = typeof(T);
            // 仅选择协议字段属性：排除框架属性（Descriptor/Parser等）
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead)
                         .Where(p => !IsFrameworkProperty(p.Name))
                         .Where(p => IsRenderableType(p.PropertyType));

            foreach (var p in props)
            {
                object v = null;
                try { v = p.GetValue(Data); }
                catch { }
                var s = FormatValue(v);
                properties.Add((p.Name, s));
            }
        }

        private static bool IsFrameworkProperty(string name)
        {
            switch (name)
            {
                case nameof(IMessage.Descriptor):
                case "Parser":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsRenderableType(Type type)
        {
            if (type.IsPrimitive) return true;
            if (type == typeof(string)) return true;
            if (type == typeof(ByteString)) return true;
            if (type.IsEnum) return true;
            if (typeof(IMessage).IsAssignableFrom(type)) return false; // 嵌套消息不直接渲染
            // 支持 RepeatedField<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "Google.Protobuf.Collections.RepeatedField`1")
                return true;
            return true; // 其他类型用 ToString() 兜底
        }

        private static string FormatValue(object v)
        {
            if (v == null) return "";
            switch (v)
            {
                case bool b:
                    return b ? "Yes" : "No";
                case ByteString bs:
                    return $"{bs.Length} bytes";
            }
            var type = v.GetType();
            // RepeatedField<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "Google.Protobuf.Collections.RepeatedField`1")
            {
                var asEnum = v as System.Collections.IEnumerable;
                if (asEnum != null)
                {
                    var items = new List<string>();
                    foreach (var item in asEnum)
                        items.Add(FormatValue(item));
                    return string.Join(", ", items);
                }
            }
            return v.ToString();
        }
    }
}
