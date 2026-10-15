using Microservices.Common.Exceptions;

namespace Microservices.Template.Contracts.Exceptions;

public class TemplateItemNotFoundException(long id)
    : NotFoundException("template_item_not_found", [id]);
