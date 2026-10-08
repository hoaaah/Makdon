using MdViewer.Tests.Support;

// Test WPF gagal bila ada galat tak terduga yang lolos ke dispatcher UI (ditelan WpfHost); lihat DispatcherErrors.
[assembly: FailOnUnexpectedDispatcherErrors]
