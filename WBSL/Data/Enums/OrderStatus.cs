using NpgsqlTypes;

namespace WBSL.Data.Enums;

public enum OrderStatus : short
{
    New = 0, // «Новое сборочное задание»
    Confirm = 1, // «На сборке»
    Complete = 2, // «В доставке»
    Cancel = 3, // «Отменено продавцом»
    Receive = 4, // «Получено клиентом»
    Reject = 5 // «Отказ клиента при получении»
}