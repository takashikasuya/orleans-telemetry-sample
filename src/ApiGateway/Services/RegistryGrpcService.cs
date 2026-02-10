using System.Linq;
using ApiGateway.Infrastructure;
using Devices.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Services;

public sealed class RegistryGrpcService : Devices.V1.RegistryService.RegistryServiceBase
{
    private readonly TagSearchService _tagSearch;
    private readonly IHttpContextAccessor _contextAccessor;

    public RegistryGrpcService(TagSearchService tagSearch, IHttpContextAccessor contextAccessor)
    {
        _tagSearch = tagSearch;
        _contextAccessor = contextAccessor;
    }

    public override async Task<TagNodeSearchResponse> SearchByTags(TagSearchRequest request, ServerCallContext context)
    {
        if (request.Tags.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tags are required."));
        }

        var tenant = TenantResolver.ResolveTenant(_contextAccessor.HttpContext);
        var response = await _tagSearch.SearchNodesByTagsAsync(
            tenant,
            request.Tags,
            request.Limit > 0 ? request.Limit : null,
            context.CancellationToken);

        var grpc = new TagNodeSearchResponse
        {
            Count = response.Count
        };

        grpc.Items.AddRange(response.Items.Select(x =>
        {
            var item = new TagNode
            {
                NodeId = x.NodeId,
                NodeType = x.NodeType.ToString(),
                DisplayName = x.DisplayName
            };

            item.MatchedTags.AddRange(x.MatchedTags);
            foreach (var pair in x.Attributes)
            {
                item.Attributes[pair.Key] = pair.Value;
            }

            return item;
        }));

        return grpc;
    }

    public override async Task<TagGrainSearchResponse> SearchGrainsByTags(TagSearchRequest request, ServerCallContext context)
    {
        if (request.Tags.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tags are required."));
        }

        var tenant = TenantResolver.ResolveTenant(_contextAccessor.HttpContext);
        var response = await _tagSearch.SearchGrainsByTagsAsync(
            tenant,
            request.Tags,
            request.Limit > 0 ? request.Limit : null,
            context.CancellationToken);

        var grpc = new TagGrainSearchResponse
        {
            Count = response.Count
        };

        grpc.Items.AddRange(response.Items.Select(x =>
        {
            var item = new TagGrain
            {
                SourceNodeId = x.SourceNodeId,
                NodeType = x.NodeType.ToString(),
                GrainType = x.GrainType,
                GrainKey = x.GrainKey
            };

            item.MatchedTags.AddRange(x.MatchedTags);
            return item;
        }));

        return grpc;
    }
}
