using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

/// <summary>
/// Northwind veritabanından türetilmiş seed verisi.
/// ID formatı: minimum 4 hane, prefix yok (order: 1030+, customer: 1001+, complaint: 1001+).
/// </summary>
public static class NorthwindSeedData
{
    public static CategoryEntity[] Categories() =>
    [
        new() { Id =  1, Name = "İçecekler"                  },
        new() { Id =  2, Name = "Çeşniler"                   },
        new() { Id =  3, Name = "Yağlar"                     },
        new() { Id =  4, Name = "Reçeller"                   },
        new() { Id =  5, Name = "Kuru Meyve & Kuruyemiş"     },
        new() { Id =  6, Name = "Soslar"                     },
        new() { Id =  7, Name = "Konserve Meyve & Sebze"     },
        new() { Id =  8, Name = "Unlu Mamuller"              },
        new() { Id =  9, Name = "Konserve Et"                },
        new() { Id = 10, Name = "Çorbalar"                   },
        new() { Id = 11, Name = "Şekerlemeler"               },
        new() { Id = 12, Name = "Tahıllar"                   },
        new() { Id = 13, Name = "Makarna"                   },
        new() { Id = 14, Name = "Süt Ürünleri"               },
        new() { Id = 15, Name = "Tahıl Gevrekleri"           },
        new() { Id = 16, Name = "Cips & Atıştrmalıklar"     },
        new() { Id = 17, Name = "Bilgisayar & Laptop"        },
        new() { Id = 18, Name = "Akıllı Telefon & Tablet"    },
        new() { Id = 19, Name = "Ses & Kulaklık"             },
        new() { Id = 20, Name = "Bilgisayar Aksesuarları"    },
        new() { Id = 21, Name = "Depolama Aygıtları"         },
    ];

    public static ProductEntity[] Products() =>
    [
        // ─── İçecekler (CategoryId=1) ───
        new() { Id =  1, Name = "Çay",                     Price = 18.00m,   Stock = 40,  CategoryId =  1 },
        new() { Id =  2, Name = "Bira",                   Price = 14.00m,   Stock = 60,  CategoryId =  1 },
        new() { Id =  3, Name = "Kahve",                  Price = 46.00m,   Stock = 100, CategoryId =  1 },
        // ─── Çeşniler (CategoryId=2) ───
        new() { Id =  4, Name = "Şurup",                  Price = 10.00m,   Stock = 100, CategoryId =  2 },
        new() { Id =  5, Name = "Cajun Baharati",         Price = 22.00m,   Stock = 40,  CategoryId =  2 },
        new() { Id =  6, Name = "Acı Biber",              Price = 26.00m,   Stock = 90,  CategoryId =  2 },
        // ─── Yağlar (CategoryId=3) ───
        new() { Id =  7, Name = "Zeytinyağı",             Price = 21.35m,   Stock = 40,  CategoryId =  3 },
        // ─── Reçeller (CategoryId=4) ───
        new() { Id =  8, Name = "Boysenberry Reçeli",     Price = 25.00m,   Stock = 100, CategoryId =  4 },
        new() { Id =  9, Name = "Marmelat",               Price = 81.00m,   Stock = 40,  CategoryId =  4 },
        // ─── Kuru Meyve & Kuruyemiş (CategoryId=5) ───
        new() { Id = 10, Name = "Kuru Armut",             Price = 30.00m,   Stock = 40,  CategoryId =  5 },
        new() { Id = 11, Name = "Ceviz",                  Price = 23.25m,   Stock = 40,  CategoryId =  5 },
        new() { Id = 12, Name = "Kuru Elma",              Price = 9.65m,    Stock = 50,  CategoryId =  5 },
        // ─── Soslar (CategoryId=6) ───
        new() { Id = 13, Name = "Köri Sosu",              Price = 40.00m,   Stock = 40,  CategoryId =  6 },
        // ─── Konserve Meyve & Sebze (CategoryId=7) ───
        new() { Id = 14, Name = "Meyve Kokteyli",         Price = 39.00m,   Stock = 40,  CategoryId =  7 },
        new() { Id = 15, Name = "Mantar",                 Price = 27.00m,   Stock = 100, CategoryId =  7 },
        // ─── Unlu Mamuller (CategoryId=8) ───
        new() { Id = 16, Name = "Çikolatalı Bisküvi Karışımı", Price = 9.20m, Stock = 20, CategoryId =  8 },
        new() { Id = 17, Name = "Scones",                 Price = 10.00m,   Stock = 20,  CategoryId =  8 },
        new() { Id = 18, Name = "Kurabiye",               Price = 7.00m,    Stock = 100, CategoryId =  8 },
        // ─── Konserve Et (CategoryId=9) ───
        new() { Id = 19, Name = "Yengeç Eti",             Price = 18.40m,   Stock = 120, CategoryId =  9 },
        new() { Id = 20, Name = "Gravad Lax",             Price = 3.50m,    Stock = 100, CategoryId =  9 },
        new() { Id = 21, Name = "Kaviar",                 Price = 2.99m,    Stock = 200, CategoryId =  9 },
        // ─── Şekerlemeler (CategoryId=11) ───
        new() { Id = 22, Name = "Çikolata",               Price = 12.75m,   Stock = 100, CategoryId = 11 },
        new() { Id = 23, Name = "Butterscotch",           Price = 53.00m,   Stock = 60,  CategoryId = 11 },
        // ─── Makarna (CategoryId=13) ───
        new() { Id = 24, Name = "Ravioli",                Price = 19.50m,   Stock = 100, CategoryId = 13 },
        new() { Id = 25, Name = "Makarna",                Price = 38.00m,   Stock = 50,  CategoryId = 13 },
        // ─── Süt Ürünleri (CategoryId=14) ───
        new() { Id = 26, Name = "Mozzarella",             Price = 34.80m,   Stock = 50,  CategoryId = 14 },
        new() { Id = 27, Name = "Geitost",                Price = 10.00m,   Stock = 40,  CategoryId = 14 },
        new() { Id = 28, Name = "Mascarpone",             Price = 55.40m,   Stock = 80,  CategoryId = 14 },
        // ─── Bilgisayar & Laptop (CategoryId=17) ───
        new() { Id = 29, Name = "Laptop",                 Price = 999.99m,  Stock = 25,  CategoryId = 17 },
        // ─── Akıllı Telefon & Tablet (CategoryId=18) ───
        new() { Id = 30, Name = "Akıllı Telefon",         Price = 699.99m,  Stock = 50,  CategoryId = 18 },
        new() { Id = 31, Name = "Tablet",                 Price = 449.99m,  Stock = 30,  CategoryId = 18 },
        // ─── Ses & Kulaklık (CategoryId=19) ───
        new() { Id = 32, Name = "Kablosuz Kulaklık",      Price = 149.99m,  Stock = 75,  CategoryId = 19 },
        // ─── Bilgisayar Aksesuarları (CategoryId=20) ───
        new() { Id = 33, Name = "Kablosuz Mouse",         Price = 49.99m,   Stock = 100, CategoryId = 20 },
        new() { Id = 34, Name = "Mekanik Klavye",         Price = 89.99m,   Stock = 60,  CategoryId = 20 },
        new() { Id = 35, Name = "USB-C Hub",              Price = 39.99m,   Stock = 90,  CategoryId = 20 },
        // ─── Depolama Aygıtları (CategoryId=21) ───
        new() { Id = 36, Name = "Harici SSD",             Price = 79.99m,   Stock = 80,  CategoryId = 21 },
    ];

    // Customer ID aralığı: 1001–1029 (Northwind customer 1–29)
    // Order ID aralığı: 1030–1081 (Northwind order 30–81)
    // status_id: 0,1 → Processing | 2 → Shipped | 3 → Delivered
    public static OrderEntity[] Orders() =>
    [
        new() { Code=1030, CustomerId=1027, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,1,15)  },
        new() { Code=1031, CustomerId=1004, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,1,20)  },
        new() { Code=1032, CustomerId=1012, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,1,22)  },
        new() { Code=1033, CustomerId=1008, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,1,30)  },
        new() { Code=1034, CustomerId=1004, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,2,6)   },
        new() { Code=1035, CustomerId=1029, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,2,10)  },
        new() { Code=1036, CustomerId=1003, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,2,23)  },
        new() { Code=1037, CustomerId=1006, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,3,6)   },
        new() { Code=1038, CustomerId=1028, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,3,10)  },
        new() { Code=1039, CustomerId=1008, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,3,22)  },
        new() { Code=1040, CustomerId=1010, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,3,24)  },
        new() { Code=1041, CustomerId=1007, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,3,24)  },
        new() { Code=1042, CustomerId=1010, Status=WellKnown.OrderStatuses.Shipped,    OrderDate=D(2006,3,24)  },
        new() { Code=1043, CustomerId=1011, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,3,24)  },
        new() { Code=1044, CustomerId=1001, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,3,24)  },
        new() { Code=1045, CustomerId=1028, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,7)   },
        new() { Code=1046, CustomerId=1009, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,5)   },
        new() { Code=1047, CustomerId=1006, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,8)   },
        new() { Code=1048, CustomerId=1008, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,5)   },
        new() { Code=1050, CustomerId=1025, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,5)   },
        new() { Code=1051, CustomerId=1026, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,5)   },
        new() { Code=1055, CustomerId=1029, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,5)   },
        new() { Code=1056, CustomerId=1006, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,3)   },
        new() { Code=1057, CustomerId=1027, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,4,22)  },
        new() { Code=1058, CustomerId=1004, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,22)  },
        new() { Code=1059, CustomerId=1012, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,4,22)  },
        new() { Code=1060, CustomerId=1008, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,30)  },
        new() { Code=1061, CustomerId=1004, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,4,7)   },
        new() { Code=1062, CustomerId=1029, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,4,12)  },
        new() { Code=1063, CustomerId=1003, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,4,25)  },
        new() { Code=1064, CustomerId=1006, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,9)   },
        new() { Code=1065, CustomerId=1028, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,11)  },
        new() { Code=1066, CustomerId=1008, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,24)  },
        new() { Code=1067, CustomerId=1010, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,5,24)  },
        new() { Code=1068, CustomerId=1007, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,24)  },
        new() { Code=1069, CustomerId=1010, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,24)  },
        new() { Code=1070, CustomerId=1011, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,24)  },
        new() { Code=1071, CustomerId=1001, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,5,24)  },
        new() { Code=1072, CustomerId=1028, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,7)   },
        new() { Code=1073, CustomerId=1009, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,5)   },
        new() { Code=1074, CustomerId=1006, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,8)   },
        new() { Code=1075, CustomerId=1008, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,5)   },
        new() { Code=1076, CustomerId=1025, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,5)   },
        new() { Code=1077, CustomerId=1026, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,5)   },
        new() { Code=1078, CustomerId=1029, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,5)   },
        new() { Code=1079, CustomerId=1006, Status=WellKnown.OrderStatuses.Delivered,  OrderDate=D(2006,6,23)  },
        new() { Code=1080, CustomerId=1004, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,4,25)  },
        new() { Code=1081, CustomerId=1003, Status=WellKnown.OrderStatuses.Processing, OrderDate=D(2006,4,25)  },
    ];

    // ProductId referansı: bkz. Products() — Id=1:Çay 2:Bira 3:Kahve 4:Şurup 5:Cajun
    //  6:AcıBiber 7:Zeytiny. 8:BoysenberryReç. 9:Marmelat 10:KuruArmut
    //  11:Ceviz 12:KuruElma 13:KöriSosu 14:MeyveKokteyli 15:Mantar
    //  16:Çik.Bisc. 17:Scones 18:Kurabiye 19:YengeçEti 20:GravadLax
    //  21:Kaviar 22:Çikolata 23:Butterscotch 24:Ravioli 25:Makarna
    //  26:Mozzarella 27:Geitost 28:Mascarpone 29:Laptop 30:AkıllıTelefon
    //  31:Tablet 32:KablosuzKulaklık 33:KablosuzMouse 34:MekanikKlavye
    //  35:USB-CHub 36:HariciSSD
    public static OrderDetailEntity[] OrderDetails() =>
    [
        new() { OrderCode=1030, ProductId= 2, Quantity=100 }, // Bira
        new() { OrderCode=1031, ProductId=10, Quantity= 10 }, // Kuru Armut
        new() { OrderCode=1032, ProductId= 1, Quantity= 15 }, // Çay
        new() { OrderCode=1033, ProductId=16, Quantity= 30 }, // Çik. Bisküvi Kar.
        new() { OrderCode=1034, ProductId=16, Quantity= 20 }, // Çik. Bisküvi Kar.
        new() { OrderCode=1035, ProductId=22, Quantity= 10 }, // Çikolata
        new() { OrderCode=1036, ProductId=12, Quantity=200 }, // Kuru Elma
        new() { OrderCode=1037, ProductId=13, Quantity= 17 }, // Köri Sosu
        new() { OrderCode=1038, ProductId= 3, Quantity=300 }, // Kahve
        new() { OrderCode=1039, ProductId=22, Quantity=100 }, // Çikolata
        new() { OrderCode=1040, ProductId=21, Quantity=200 }, // Kaviar
        new() { OrderCode=1041, ProductId= 3, Quantity=300 }, // Kahve
        new() { OrderCode=1042, ProductId= 8, Quantity= 10 }, // Boysenberry Reçeli
        new() { OrderCode=1043, ProductId=20, Quantity= 20 }, // Gravad Lax
        new() { OrderCode=1044, ProductId= 1, Quantity= 25 }, // Çay
        new() { OrderCode=1045, ProductId=12, Quantity= 50 }, // Kuru Elma
        new() { OrderCode=1046, ProductId=24, Quantity=100 }, // Ravioli
        new() { OrderCode=1047, ProductId= 2, Quantity=300 }, // Bira
        new() { OrderCode=1048, ProductId=13, Quantity= 25 }, // Köri Sosu
        new() { OrderCode=1050, ProductId=17, Quantity= 20 }, // Scones
        new() { OrderCode=1051, ProductId= 7, Quantity= 25 }, // Zeytinyağı
        new() { OrderCode=1055, ProductId= 2, Quantity= 87 }, // Bira
        new() { OrderCode=1056, ProductId=22, Quantity= 10 }, // Çikolata
        new() { OrderCode=1057, ProductId= 1, Quantity=  1 }, // Çay
        new() { OrderCode=1058, ProductId= 9, Quantity= 40 }, // Marmelat
        new() { OrderCode=1059, ProductId= 3, Quantity=  1 }, // Kahve
        new() { OrderCode=1060, ProductId=26, Quantity= 40 }, // Mozzarella
        new() { OrderCode=1061, ProductId= 4, Quantity=  1 }, // Şurup
        new() { OrderCode=1062, ProductId=11, Quantity=  1 }, // Ceviz
        new() { OrderCode=1063, ProductId= 4, Quantity= 50 }, // Şurup
        new() { OrderCode=1064, ProductId= 2, Quantity=  1 }, // Bira
        new() { OrderCode=1065, ProductId= 3, Quantity=  1 }, // Kahve
        new() { OrderCode=1066, ProductId=22, Quantity=  1 }, // Çikolata
        new() { OrderCode=1067, ProductId=27, Quantity= 20 }, // Geitost
        new() { OrderCode=1068, ProductId=17, Quantity=  1 }, // Scones
        new() { OrderCode=1069, ProductId=20, Quantity= 15 }, // Gravad Lax
        new() { OrderCode=1070, ProductId=13, Quantity= 20 }, // Köri Sosu
        new() { OrderCode=1071, ProductId=19, Quantity= 40 }, // Yengeç Eti
        new() { OrderCode=1072, ProductId= 3, Quantity=  5 }, // Kahve
        new() { OrderCode=1073, ProductId=12, Quantity= 10 }, // Kuru Elma
        new() { OrderCode=1074, ProductId=22, Quantity= 40 }, // Çikolata
        new() { OrderCode=1075, ProductId=22, Quantity= 40 }, // Çikolata
        new() { OrderCode=1076, ProductId= 5, Quantity= 30 }, // Cajun Baharati
        new() { OrderCode=1077, ProductId= 8, Quantity= 90 }, // Boysenberry Reçeli
        new() { OrderCode=1078, ProductId=14, Quantity= 40 }, // Meyve Kokteyli
        new() { OrderCode=1079, ProductId=10, Quantity= 30 }, // Kuru Armut
        new() { OrderCode=1080, ProductId=24, Quantity= 10 }, // Ravioli
        new() { OrderCode=1081, ProductId=24, Quantity=  1 }, // Ravioli
    ];

    public static CustomerEntity[] Customers() =>
    [
        new() { Id = 1001, FullName = "Maria Anders",         Email = "maria.anders@example.com",       Phone = "+49-030-0074321"  },
        new() { Id = 1002, FullName = "Ana Trujillo",         Email = "ana.trujillo@example.com",       Phone = "+52-5-555-4729"   },
        new() { Id = 1003, FullName = "Antonio Moreno",       Email = "antonio.moreno@example.com",     Phone = "+52-5-555-3932"   },
        new() { Id = 1004, FullName = "Thomas Hardy",         Email = "thomas.hardy@example.com",       Phone = "+44-171-555-7788" },
        new() { Id = 1005, FullName = "Christina Berglund",   Email = "christina.berglund@example.com", Phone = "+46-0921-12 34 65"},
        new() { Id = 1006, FullName = "Hanna Moos",           Email = "hanna.moos@example.com",         Phone = "+49-0621-08460"   },
        new() { Id = 1007, FullName = "Frederique Citeaux",   Email = "frederique.citeaux@example.com", Phone = "+33-88.60.15.31"  },
        new() { Id = 1008, FullName = "Martin Sommer",        Email = "martin.sommer@example.com",      Phone = "+34-91 745 6200"  },
        new() { Id = 1009, FullName = "Laurence Lebihan",     Email = "laurence.lebihan@example.com",   Phone = "+33-91.24.45.40"  },
        new() { Id = 1010, FullName = "Elizabeth Lincoln",    Email = "elizabeth.lincoln@example.com",  Phone = "+1-604-555-4729"  },
        new() { Id = 1011, FullName = "Victoria Ashworth",    Email = "victoria.ashworth@example.com",  Phone = "+44-171-555-1212" },
        new() { Id = 1012, FullName = "Patricio Simpson",     Email = "patricio.simpson@example.com",   Phone = "+54-1-135-5555"   },
        new() { Id = 1013, FullName = "Francisco Chang",      Email = "francisco.chang@example.com",    Phone = "+52-5-555-3392"   },
        new() { Id = 1014, FullName = "Yang Wang",            Email = "yang.wang@example.com",          Phone = "+41-0452-076545"  },
        new() { Id = 1015, FullName = "Pedro Afonso",         Email = "pedro.afonso@example.com",       Phone = "+55-21-555-9857"  },
        new() { Id = 1016, FullName = "Elizabeth Brown",      Email = "elizabeth.brown@example.com",    Phone = "+44-171-555-2282" },
        new() { Id = 1017, FullName = "Sven Ottlieb",         Email = "sven.ottlieb@example.com",       Phone = "+49-0241-02460"   },
        new() { Id = 1018, FullName = "Janine Labrune",       Email = "janine.labrune@example.com",     Phone = "+33-40.67.88.88"  },
        new() { Id = 1019, FullName = "Ann Devon",            Email = "ann.devon@example.com",          Phone = "+44-171-555-0297" },
        new() { Id = 1020, FullName = "Roland Mendel",        Email = "roland.mendel@example.com",      Phone = "+43-7675-3425"    },
        new() { Id = 1021, FullName = "Aria Cruz",            Email = "aria.cruz@example.com",          Phone = "+55-11-555-9857"  },
        new() { Id = 1022, FullName = "Diego Roel",           Email = "diego.roel@example.com",         Phone = "+34-91 745 6210"  },
        new() { Id = 1023, FullName = "Martine Rance",        Email = "martine.rance@example.com",      Phone = "+46-0695-34 67 21"},
        new() { Id = 1024, FullName = "Maria Larsson",        Email = "maria.larsson@example.com",      Phone = "+46-0695-34 67 22"},
        new() { Id = 1025, FullName = "Peter Franken",        Email = "peter.franken@example.com",      Phone = "+49-089-0877310"  },
        new() { Id = 1026, FullName = "Carine Schmitt",       Email = "carine.schmitt@example.com",     Phone = "+33-99.74.44.60"  },
        new() { Id = 1027, FullName = "Paolo Accorti",        Email = "paolo.accorti@example.com",      Phone = "+39-011-4988260"  },
        new() { Id = 1028, FullName = "Lino Rodriguez",       Email = "lino.rodriguez@example.com",     Phone = "+351-2-202346"    },
        new() { Id = 1029, FullName = "Eduardo Saavedra",     Email = "eduardo.saavedra@example.com",   Phone = "+34-93-203 4560"  },
    ];

    public static ComplaintEntity[] Complaints() =>
    [
        new() { Code=1001, OrderId=1033, CustomerId=1008, Complaint="Ürün hasarlı paketlenmiş olarak geldi.",                  Status=WellKnown.ComplaintStatuses.Resolved   },
        new() { Code=1002, OrderId=1038, CustomerId=1028, Complaint="Sipariş tahmini teslimat süresini aştı.",                  Status=WellKnown.ComplaintStatuses.Pending    },
        new() { Code=1003, OrderId=1040, CustomerId=1010, Complaint="Gönderilen ürün sipariş ettiğimden farklı.",              Status=WellKnown.ComplaintStatuses.InProgress },
        new() { Code=1004, OrderId=1048, CustomerId=1008, Complaint="Ürünün son kullanma tarihi geçmişti.",                    Status=WellKnown.ComplaintStatuses.Resolved   },
        new() { Code=1005, OrderId=1060, CustomerId=1008, Complaint="Paket eksik geldi, iki ürün yerine bir ürün teslim alındı.", Status=WellKnown.ComplaintStatuses.Pending },
    ];

    private static DateTime D(int y, int m, int d) =>
        new(y, m, d, 0, 0, 0, DateTimeKind.Utc);
}
