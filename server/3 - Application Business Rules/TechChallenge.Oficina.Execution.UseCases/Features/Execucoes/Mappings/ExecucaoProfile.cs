using AutoMapper;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Mappings;

public sealed class ExecucaoProfile : Profile
{
    public ExecucaoProfile()
    {
        CreateMap<Execucao, ExecucaoViewModel>()
            .ForMember(destino => destino.StatusOrdemServico, configuracao => configuracao.MapFrom(origem => (int)origem.StatusOrdemServico))
            .ForMember(destino => destino.StatusOrdemServicoDescricao, configuracao => configuracao.MapFrom(origem => origem.StatusOrdemServico.ObterDescricao()))
            .ForMember(destino => destino.Etapa, configuracao => configuracao.MapFrom(origem => (int)origem.Etapa))
            .ForMember(destino => destino.EtapaDescricao, configuracao => configuracao.MapFrom(origem => origem.Etapa.ObterDescricao()));
    }
}
