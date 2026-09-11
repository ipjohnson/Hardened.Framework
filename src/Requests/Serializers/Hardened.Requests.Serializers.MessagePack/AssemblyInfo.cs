using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Serializers.MessagePack;

// What HRDR012 reads. Without it every operation declaring this media type warns that nothing
// writes it, including the operations that reference this package - see SerializerContentTypes.
[assembly: WritesContentType(MessagePackContentType.Value)]
