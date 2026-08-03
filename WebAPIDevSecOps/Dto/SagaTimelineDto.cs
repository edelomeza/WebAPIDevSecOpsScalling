namespace WebAPIDevSecOps.Dto
{
    public class SagaTimelineDto
    {
        public Guid id { get; set; }
        public string strEstadoSaga { get; set; } = null!;
        public string? strMotivoRechazo { get; set; }
        public List<SagaEventDto> lstEventos { get; set; } = new();
    }

    public class SagaEventDto
    {
        public string strEtapa { get; set; } = null!;
        public DateTime? dteFecha { get; set; }
        public string strEstado { get; set; } = null!;
        public string? strDetalle { get; set; }
    }
}
